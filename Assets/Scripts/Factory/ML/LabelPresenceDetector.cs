using System;
using UnityEngine;
using Unity.InferenceEngine;

/// <summary>같은 이진 분류 모델로 5개 카메라를 검사한다. 입력: RGB 0~1, NCHW, 위에서 아래 순서.</summary>
[DisallowMultipleComponent]
public sealed class LabelPresenceDetector : MonoBehaviour
{
    [SerializeField] ModelAsset model;
    [SerializeField, Range(0f, 1f)] float threshold = 0.5f;
    Worker worker;
    ModelAsset loadedModel;
    public bool HasModel => model != null;
    public double LastMilliseconds { get; private set; }
    public string ModelName => model != null ? model.name : "미연결";
    public float[] Scores { get; } = new float[5];
    public BoxFace PredictedFace { get; private set; } = BoxFace.None;
    public event Action<BoxFace> FaceJudged;

    public BoxFace Judge(FaceCameraRig rig)
    {
        if (rig == null || !rig.IsBuilt) throw new ArgumentException("카메라 리그 5대가 준비되지 않았습니다.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var images = rig.Capture();
        try { return Judge(images); }
        finally { FaceCameraRig.ReleaseShots(images); LastMilliseconds = clock.Elapsed.TotalMilliseconds; }
    }

    public bool ValidateConfiguration(out string error)
    {
        try { Prepare(); error = null; return true; }
        catch (Exception exception) { error = exception.Message; return false; }
    }

    void Prepare()
    {
        if (!isActiveAndEnabled) throw new InvalidOperationException("송장 판정 모듈이 비활성 상태입니다.");
        if (model == null) throw new InvalidOperationException("송장 유무 ONNX를 연결하세요.");
        if (threshold <= 0f || threshold >= 1f) throw new InvalidOperationException("검출 기준은 0과 1 사이여야 합니다.");
        if (worker != null && loadedModel == model) return;
        DisposeWorker();
        var network = ModelLoader.Load(model);
        if (network.inputs.Count != 1 || network.outputs.Count != 1 || network.inputs[0].name != "image" ||
            network.outputs[0].name != "label_score" || network.inputs[0].dataType != DataType.Float ||
            !network.inputs[0].shape.IsStatic() || !network.inputs[0].shape.ToTensorShape().Equals(new TensorShape(1, 3, 96, 96)))
            throw new InvalidOperationException("송장 유무 모델 규약 불일치: image [1,3,96,96] RGB → label_score [1,1] 모델이 필요합니다.");
        worker = new Worker(network, BackendType.CPU);
        loadedModel = model;
    }

    /// <summary>정지한 공정 상자만 촬영해 판정과 점수를 기록한다. 실패는 Bottom으로 숨기지 않는다.</summary>
    public bool TryJudgeBox(BoxInfo box, FaceCameraRig rig, out string error)
    {
        error = null;
        if (box == null || rig == null || !rig.IsBuilt) { error = "상자 또는 카메라 리그가 없습니다."; return false; }
        box.judgedFace = BoxFace.None;
        int layer = LayerMask.NameToLayer(SampleBox.DefaultLayerName);
        if (layer < 0 || rig.CullingMask.value != (1 << layer))
        { error = "공정 촬영은 ScanTarget 레이어만 보는 카메라가 필요합니다."; return false; }
        var children = box.GetComponentsInChildren<Transform>(true);
        var layers = new int[children.Length];
        for (int i = 0; i < children.Length; i++) { layers[i] = children[i].gameObject.layer; children[i].gameObject.layer = layer; }
        try
        {
            rig.EnsureCameraOrder();
            var face = Judge(rig);
            if (box.faceRatios == null || box.faceRatios.Length != 5) box.faceRatios = new float[5];
            for (int i = 0; i < 5; i++) box.faceRatios[(int)FaceCameraRig.CapturedFaces[i]] = Scores[i];
            box.judgedFace = face;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Array.Clear(Scores, 0, Scores.Length);
            return false;
        }
        finally
        {
            for (int i = 0; i < children.Length; i++) if (children[i] != null) children[i].gameObject.layer = layers[i];
        }
    }

    public BoxFace Judge(Texture2D[] images)
    {
        if (images == null || images.Length != 5) throw new ArgumentException("카메라 영상 5장이 필요합니다.");
        Prepare();
        int best = -1;
        float highest = threshold;
        for (int camera = 0; camera < 5; camera++)
        {
            var image = images[camera];
            const int size = 96;
            if (image == null || image.width != size || image.height != size)
                throw new ArgumentException("송장 유무 모델은 96×96 영상을 사용합니다.");
            var pixels = image.GetPixels32();
            var data = new float[3 * size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Unity GetPixels32는 아래에서 위, PNG/Pillow 학습은 위에서 아래다.
                    var pixel = pixels[(size - 1 - y) * size + x];
                    int offset = y * size + x;
                    data[offset] = pixel.r / 255f;
                    data[size * size + offset] = pixel.g / 255f;
                    data[2 * size * size + offset] = pixel.b / 255f;
                }
            using (var input = new Tensor<float>(new TensorShape(1, 3, size, size), data))
            {
                worker.Schedule(input);
                var tensor = worker.PeekOutput() as Tensor<float>;
                if (tensor == null || tensor.shape.length != 1) throw new InvalidOperationException("모델 출력은 float 점수 한 개여야 합니다.");
                using (var output = tensor.ReadbackAndClone()) Scores[camera] = output[0];
            }
            if (float.IsNaN(Scores[camera]) || float.IsInfinity(Scores[camera]) || Scores[camera] < 0f || Scores[camera] > 1f)
                throw new InvalidOperationException("모델 검출 점수가 유효하지 않습니다.");
            if (Scores[camera] >= highest) { highest = Scores[camera]; best = camera; }
        }
        PredictedFace = best < 0 ? BoxFace.Bottom : FaceCameraRig.CapturedFaces[best];
        FaceJudged?.Invoke(PredictedFace);
        return PredictedFace;
    }

    void DisposeWorker() { worker?.Dispose(); worker = null; loadedModel = null; }
    public void Release() => DisposeWorker();
    void OnDisable() => DisposeWorker();
}
