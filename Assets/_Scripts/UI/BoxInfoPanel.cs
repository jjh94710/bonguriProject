using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BoxInfoPanel : MonoBehaviour
{
    [Header("표시할 박스")]
    [InspectorName("판독 구간 (ReadLine)")]
    [Tooltip("판독 위치에 도착한 박스로 즉시 전환. 떠난 뒤에도 마지막 박스를 표시합니다.")]
    [SerializeField] ConveyorLine correctionLine;
    [Tooltip("공정 벨트가 연결되지 않은 수동 시험에서만 표시할 박스")]
    [SerializeField] BoxInfo previewBox;
    [Header("표시 항목")]
    [SerializeField] TMP_Text sourceText, boxText, labelText, destinationText, correctionText;
    [SerializeField, Min(.05f)] float refreshInterval = .2f;
    [SerializeField] Camera[] viewCameras;
    float nextRefresh;
    BoxInfo trackedBox;
    static readonly Color Normal = new Color(.92f,.95f,1f);
    static readonly Color Muted = new Color(.58f,.66f,.75f);
    static readonly Color Good = new Color(.35f,.88f,.68f);
    static readonly Color Attention = new Color(1f,.76f,.32f);
    static readonly Color Error = new Color(1f,.4f,.4f);
    void OnEnable(){ Refresh(); }
    void Update(){ if(!Application.isPlaying || Time.unscaledTime < nextRefresh)return; nextRefresh=Time.unscaledTime+refreshInterval;Refresh(); }
    public void Refresh()
    {
        var canvas=GetComponentInParent<Canvas>();
        if(canvas!=null && viewCameras!=null) foreach(var camera in viewCameras) if(camera!=null && camera.isActiveAndEnabled){canvas.targetDisplay=camera.targetDisplay;break;}
        var box = DisplayBox();
        Set(sourceText, correctionLine != null ? Stage(box) : previewBox != null ? "수동 확인" : "박스 대기", Muted);
        if(box==null){Set(boxText,"대기 중",Muted);Set(labelText,"—",Muted);Set(destinationText,"—",Muted);Set(correctionText,"박스 투입 대기",Muted);return;}
        Set(boxText,string.IsNullOrWhiteSpace(box.boxId)?box.gameObject.name:box.boxId,Normal);
        // Display recognition output only. labelNumber/labelRegion are training ground truth.
        string label=!box.hasLabel?"송장 없음":string.IsNullOrWhiteSpace(box.readText)?"미판독":box.readText+(string.IsNullOrWhiteSpace(box.readRegion)?"":" · "+box.readRegion);
        Set(labelText,label,!box.hasLabel?Attention:Normal);
        Set(destinationText,box.destination==Destination.None?"미정":box.destination==Destination.NG?"NG · 불량 배출":"터미널 "+box.destination,box.destination==Destination.NG?Error:box.destination==Destination.None?Muted:Good);
        string correction;Color color;
        if(box.reason==ExceptionReason.CorrectionFail){correction="교정 실패";color=Error;}
        else if(box.state==BoxState.Correcting){correction="교정 중 · 회전 "+box.rotationCount+"회";color=Attention;}
        else if(box.state==BoxState.Judging){correction="송장면 측정 중";color=Attention;}
        else if(box.state==BoxState.Moving){correction="교정 대기";color=Muted;}
        else if(box.reason!=ExceptionReason.None){correction="예외 처리 · 회전 "+box.rotationCount+"회";color=Error;}
        else if(box.judgedFace==BoxFace.Top || box.rotationCount>0){correction="교정 완료 · 회전 "+box.rotationCount+"회";color=Good;}
        else {correction="미확인";color=Muted;}
        Set(correctionText,correction,color);
    }
    BoxInfo DisplayBox()
    {
        if (correctionLine == null) return previewBox;
        if (!Application.isPlaying) return correctionLine.CurrentBox;
        var current = correctionLine.CurrentBox;
        // Switch at the reading stop, regardless of the previous parcel's completion.
        // Keep its BoxInfo while the station is empty, so recognition can update live.
        if (current != null) trackedBox = current;
        return trackedBox;
    }
    static string Stage(BoxInfo box)
    {
        if (box == null) return "박스 대기";
        switch (box.state)
        {
            case BoxState.Transferring: return "판독 구간으로 이송";
            case BoxState.Reading: return "송장 판독 중";
            case BoxState.Sorting: return "분류 구간";
            case BoxState.Loading: return "수거함 적재 중";
            case BoxState.Loaded: return "적재 완료";
            case BoxState.Exception: return "NG 처리 완료";
            default: return "판독 스테이션";
        }
    }
    static void Set(TMP_Text target,string value,Color color){if(target==null)return;if(target.text!=value)target.text=value;target.color=color;}
}

