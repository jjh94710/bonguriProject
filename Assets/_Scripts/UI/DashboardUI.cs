using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DashboardUI : MonoBehaviour
{
    [SerializeField] CellController controller;
    [SerializeField] TMP_Text statusText, processedText, throughputText, exceptionRateText;
    [SerializeField] TMP_Text terminalAText, terminalBText, terminalCText;
    [SerializeField, Min(.1f)] float refreshInterval=1;
    float nextRefresh;
    void OnEnable(){Refresh();}
    void Update(){if(!Application.isPlaying||Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+refreshInterval;Refresh();}
    public void Refresh(){
        bool connected=controller!=null;
        Set(statusText,!connected?"공정 미연결":!Application.isPlaying?"운전 준비":controller.State==CellState.Running?"가동 중":controller.State==CellState.Waiting?"대기":"정지");
        Set(processedText,connected?controller.Processed.ToString("N0")+"개":"—");
        Set(throughputText,connected?controller.ThroughputPerHour.ToString("N0")+"개/h":"—");
        Set(exceptionRateText,connected?(controller.ExceptionRate*100).ToString("0.0")+"%":"—");
        var counts=connected?controller.TerminalCounts:null;
        Set(terminalAText,Count(counts,0));Set(terminalBText,Count(counts,1));Set(terminalCText,Count(counts,2));
    }
    static string Count(int[] counts,int index){return counts!=null&&counts.Length>index?counts[index].ToString("N0")+"개":"—";}
    static void Set(TMP_Text text,string value){if(text!=null&&text.text!=value)text.text=value;}
}
