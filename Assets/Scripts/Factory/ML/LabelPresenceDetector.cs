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
    public string ModelName => model != null ? model.name : "미연결";
    public float[] Scores { get; } = new float[5];
    public BoxFace PredictedFace { get; private set; } = BoxFace.None;
    public event Action<BoxFace> FaceJudged;

    public BoxFace Judge(FaceCameraRig rig)
    {
        var images = rig.Capture();
        try { return Judge(images); }
        finally { FaceCameraRig.ReleaseShots(images); }
    }

    public BoxFace Judge(Texture2D[] images)
    {
        if (model == null) throw new InvalidOperationException("송장 유무 ONNX를 연결하세요.");
        if (images == null || images.Length != 5) throw new ArgumentException("카메라 영상 5장이 필요합니다.");
        if (worker == null) worker = new Worker(ModelLoader.Load(model), BackendType.CPU);
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
                using (var output = (worker.PeekOutput() as Tensor<float>).ReadbackAndClone())
                    Scores[camera] = output[0];
            }
            if (float.IsNaN(Scores[camera]) || float.IsInfinity(Scores[camera]))
                throw new InvalidOperationException("모델 검출 점수가 유효하지 않습니다.");
            if (Scores[camera] >= highest) { highest = Scores[camera]; best = camera; }
        }
        PredictedFace = best < 0 ? BoxFace.Bottom : FaceCameraRig.CapturedFaces[best];
        FaceJudged?.Invoke(PredictedFace);
        return PredictedFace;
    }

    void OnDisable() { worker?.Dispose(); worker = null; }
}
