using System;
using System.Collections;
using System.Text;
using UnityEngine;
using Unity.InferenceEngine;

/// <summary>FR-04: 카메라 픽셀만으로 번호 CTC와 주소 지역을 추론한다.</summary>
[DisallowMultipleComponent]
public sealed class LabelReader : MonoBehaviour
{
    [SerializeField] LabelLayout layout;
    [SerializeField] CellSettings settings;
    [SerializeField] ModelAsset numberModel, regionModel;
    [SerializeField] Camera numberCamera, regionCamera;
    [SerializeField, Range(.01f,.99f)] float minConfidence = .8f;
    [SerializeField] LabelPresenceDetector presence;
    [SerializeField] FaceCameraRig faceRig;
    [SerializeField, Min(.01f)] float timeLimit = 2f;
    Worker numberWorker, regionWorker;
    ModelAsset loadedNumber, loadedRegion;
    public double LastMilliseconds { get; private set; }

    public bool ValidateConfiguration(out string error)
    {
        try { Prepare(); error=null;return true; }
        catch(Exception e) {error=e.Message;return false;}
    }
    void Prepare()
    {
        if (!isActiveAndEnabled || layout==null || settings==null || numberCamera==null || regionCamera==null || numberModel==null || regionModel==null)
            throw new InvalidOperationException("OCR: 배치·설정·판독 카메라 2대·ONNX 2개를 연결하세요.");
        if (layout.readPixels!=32 || Mathf.RoundToInt(layout.NumberBlockAspect*32)!=186)
            throw new InvalidOperationException("OCR 모델 입력은 번호 186×32, 지역명 96×32입니다.");
        if (numberWorker!=null && loadedNumber==numberModel && loadedRegion==regionModel)return;
        Dispose();
        try {numberWorker=Create(numberModel,186);regionWorker=Create(regionModel,96);loadedNumber=numberModel;loadedRegion=regionModel;}
        catch {Dispose();throw;}
    }
    static Worker Create(ModelAsset asset,int width)
    {
        var net=ModelLoader.Load(asset);
        if(net.inputs.Count!=1 || net.outputs.Count!=1 || net.inputs[0].name!="image" || net.outputs[0].name!="logits" ||
            net.inputs[0].dataType!=DataType.Float || !net.inputs[0].shape.IsStatic() ||
            !net.inputs[0].shape.ToTensorShape().Equals(new TensorShape(1,1,32,width)))
            throw new InvalidOperationException("OCR ONNX 입력/출력 규약이 다릅니다.");
        return new Worker(net,BackendType.CPU);
    }
    public IEnumerator Read(BoxInfo box)
    {
        if(box==null)yield break;
        if(box.reason!=ExceptionReason.None){box.destination=Destination.NG;yield break;}
        var timer=System.Diagnostics.Stopwatch.StartNew();
        box.readOk=false;box.readText="?????";box.readRegion="?";box.unknownCount=6;box.retryCount=0;
        box.labelFound=false;box.regionCode=-1;box.destination=Destination.None;
        if(!ValidateConfiguration(out string error)) {Debug.LogError(error,this);box.Mark(ExceptionReason.ReadFail);yield break;}
        // 윗면 확인은 실제 영상 판정으로 한다. hasLabel/labelNumber 정답은 읽지 않는다.
        if(!TryConfirmTop(box,out error))
        {Debug.LogError("OCR 윗면 확인 실패: "+error,this);box.Mark(ExceptionReason.ReadFail);yield break;}
        if(box.judgedFace!=BoxFace.Top) {box.Mark(ExceptionReason.NoLabel);yield break;}
        box.labelFound=true;
        int retries=Mathf.Clamp(settings.maxRetry,0,2);
        for(int attempt=0;attempt<=retries;attempt++)
        {
            box.retryCount=attempt;
            if(!TryReadOnce(box,attempt>0,out error)) {Debug.LogError(error,this);break;}
            LastMilliseconds=timer.Elapsed.TotalMilliseconds;
            if(timer.Elapsed.TotalSeconds>=timeLimit)break;
            if(box.unknownCount==0) {box.readOk=true;LabelValidator.Apply(box,settings);yield break;}
            yield return null;
        }
        box.readOk=false;box.Mark(ExceptionReason.ReadFail);
    }
    bool TryConfirmTop(BoxInfo box,out string error)
    {
        try
        {
            if(presence==null || faceRig==null)throw new InvalidOperationException("전용 판독 리그와 송장 유무 모델을 연결하세요.");
            faceRig.FrameBox(box);
            return presence.TryJudgeBox(box,faceRig,out error);
        }
        catch(Exception e){error=e.Message;return false;}
    }

    /// <summary>송장 존재 확인 뒤 호출한다. 테스트도 동일 촬영·전처리·ONNX 경로를 사용한다.</summary>
    public bool TryReadOnce(BoxInfo box,bool jitter,out string error)
    {
        error=null;
        if(box==null || box.label==null){error="OCR 상자/송장 좌표가 없습니다.";return false;}
        var children=box.GetComponentsInChildren<Transform>(true);var layers=new int[children.Length];
        for(int i=0;i<children.Length;i++)layers[i]=children[i].gameObject.layer;
        Texture2D num=null,reg=null;
        try
        {
            Prepare();int layer=LayerMask.NameToLayer(SampleBox.DefaultLayerName);
            if(layer<0)throw new InvalidOperationException("ScanTarget 레이어가 없습니다.");
            for(int i=0;i<children.Length;i++)children[i].gameObject.layer=layer;
            ConfigureCamera(numberCamera,layer);ConfigureCamera(regionCamera,layer);
            layout.AimAtNumber(numberCamera,box.label,jitter);layout.AimAtRegion(regionCamera,box.label,jitter);
            num=LabelReaderDataset.Shoot(numberCamera,186,32);reg=LabelReaderDataset.Shoot(regionCamera,96,32);
            var n=Infer(numberWorker,num);var r=Infer(regionWorker,reg);
            box.readText=DecodeNumber(n,minConfidence);
            if(box.readText.Length<5)box.readText=box.readText.PadRight(5,'?');
            else if(box.readText.Length>5)box.readText="?????";
            if(r.Length!=3)throw new InvalidOperationException("지역명 출력은 [1,3]이어야 합니다.");
            int region=Best(r,0,3,out float confidence);
            box.readRegion=confidence>=minConfidence ? new[]{"서울","대전","부산"}[region] : "?";
            box.unknownCount=box.readRegion=="?"?1:0;
            foreach(char c in box.readText)if(c=='?')box.unknownCount++;
            if(box.readText.Length!=5)box.unknownCount+=Math.Abs(5-box.readText.Length);
            return true;
        }
        catch(Exception e){error=e.Message;box.readText="?????";box.readRegion="?";box.unknownCount=6;return false;}
        finally
        {
            // Prepare 실패 시에도 원래 레이어를 보존한다.
            ReleaseShot(num);ReleaseShot(reg);
            for(int i=0;i<children.Length;i++)if(children[i]!=null)children[i].gameObject.layer=layers[i];
        }
    }
    static void ReleaseShot(Texture2D shot){if(shot==null)return;if(Application.isPlaying)Destroy(shot);else DestroyImmediate(shot);}
    static void ConfigureCamera(Camera camera,int layer)
    {camera.enabled=false;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.gray;camera.allowHDR=false;camera.allowMSAA=false;}
    static float[] Infer(Worker worker,Texture2D image)
    {
        var pixels=image.GetPixels32();var data=new float[pixels.Length];
        for(int y=0;y<image.height;y++)for(int x=0;x<image.width;x++)data[y*image.width+x]=pixels[(image.height-1-y)*image.width+x].r/255f;
        using(var input=new Tensor<float>(new TensorShape(1,1,image.height,image.width),data))
        {worker.Schedule(input);var output=worker.PeekOutput() as Tensor<float>;if(output==null)throw new InvalidOperationException("OCR 출력이 float가 아닙니다.");using(var cpu=output.ReadbackAndClone()){var values=new float[cpu.shape.length];for(int i=0;i<values.Length;i++){values[i]=cpu[i];if(float.IsNaN(values[i])||float.IsInfinity(values[i]))throw new InvalidOperationException("OCR 출력이 유효하지 않습니다.");}return values;}}
    }
    public static string DecodeNumber(float[] logits,float threshold)
    {
        if(logits==null || logits.Length!=46*11)throw new ArgumentException("번호 출력은 [1,46,11]이어야 합니다.");
        var text=new StringBuilder();int previous=10;float peak=0;
        for(int t=0;t<=46;t++)
        {
            float confidence=0;int digit=t<46?Best(logits,t*11,11,out confidence):10;
            if(digit!=previous)
            {
                if(previous!=10)text.Append(peak>=threshold?(char)('0'+previous):'?');
                previous=digit;peak=confidence;
            }
            else peak=Mathf.Max(peak,confidence);
        }
        return text.ToString();
    }
    static int Best(float[] logits,int offset,int count,out float confidence)
    {
        if(logits.Length<offset+count)throw new ArgumentException("OCR 출력 크기가 다릅니다.");
        int best=0;for(int i=1;i<count;i++)if(logits[offset+i]>logits[offset+best])best=i;
        double sum=0;for(int i=0;i<count;i++)sum+=Math.Exp(logits[offset+i]-logits[offset+best]);confidence=(float)(1/sum);return best;
    }
    public void Dispose(){numberWorker?.Dispose();regionWorker?.Dispose();numberWorker=null;regionWorker=null;loadedNumber=null;loadedRegion=null;}
    void OnDisable()=>Dispose();
}
