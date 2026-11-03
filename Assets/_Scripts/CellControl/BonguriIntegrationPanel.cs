using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Small, explicit commissioning runs. Terminal arrivals are checkpoints, not completed loads.</summary>
public sealed class BonguriIntegrationPanel : MonoBehaviour
{
    public CellController cell;
    public BoxFeeder feeder;
    public ConveyorLine[] terminalLines;
    public string status = "통합 연결 준비 · 상자 3개 시험";
    bool testing;
    float started;
    string reportFolder;
    readonly System.Collections.Generic.List<string> events = new System.Collections.Generic.List<string>();
    void OnEnable() { Application.logMessageReceived += Log; }
    void OnDisable() { Application.logMessageReceived -= Log; }
    void Log(string message, string stack, LogType type)
    {
        if (!testing || !(message.StartsWith("[Cell") || message.StartsWith("[Feeder") || message.StartsWith("[Conveyor") || message.StartsWith("[Pusher") || type == LogType.Exception || type == LogType.Error)) return;
        events.Add(Time.time.ToString("F3") + " " + type + " " + message);
    }
    public void RunSmoke(bool side = false)
    {
        if (testing || cell == null || cell.IsRunning || feeder.InProcess > 0) return;
        StartCoroutine(Smoke(side));
    }
    IEnumerator Smoke(bool side)
    {
        testing=true;started=Time.time;events.Clear();
        reportFolder="results/bonguri_integration/"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+(side?"_side":"_top");
        Directory.CreateDirectory(reportFolder);
        var s=cell.Settings;
        float top=s.faceTop, sides=s.faceSide, bottom=s.faceBottom, interval=s.feedInterval;
        s.faceTop=side?0:1;s.faceSide=side?.25f:0;s.faceBottom=0;s.feedInterval=3;
        UnityEngine.Random.InitState(20261103);
        status=side?"측면 1개 · 실제 교정 로봇 시험":"윗면 3개 · 투입→ONNX→OCR→분류 시험";
        cell.Run(side?1:3);
        while(Time.time-started < (side?100:45))
        {
            WriteReport(false);
            if(feeder.FedCount >= (side?1:3) && terminalLines.Where(l=>l!=null).Sum(l=>l.QueueCount)>= (side?1:3)) break;
            if (!cell.IsRunning && Time.time-started>2) break;
            yield return new WaitForSeconds(1);
        }
        cell.RequestCycleStop();
        s.faceTop=top;s.faceSide=sides;s.faceBottom=bottom;s.feedInterval=interval;
        status="시험 기록 저장 · 터미널 도착은 적재 완료와 별도";
        WriteReport(true);testing=false;
    }
    void WriteReport(bool complete)
    {
        var boxes=FindObjectsByType<BoxInfo>(FindObjectsSortMode.None).Where(b=>!string.IsNullOrEmpty(b.boxId)).OrderBy(b=>b.boxId).Select(b=>new BoxRecord{
            id=b.boxId,initialFace=b.initialFace.ToString(),judgedFace=b.judgedFace.ToString(),read=b.readText,region=b.readRegion,destination=b.destination.ToString(),reason=b.reason.ToString(),state=b.state.ToString(),rotations=b.rotationCount,position=b.transform.position}).ToArray();
        var report=new Report { complete=complete,elapsed=Time.time-started,fed=feeder.FedCount,processed=cell.Processed,status=status,boxes=boxes,
            terminalCounts=terminalLines.Select(l=>l==null?0:l.QueueCount).ToArray() };
        File.WriteAllText(reportFolder+"/report.json",JsonUtility.ToJson(report,true));
        File.WriteAllLines(reportFolder+"/events.log",events);
    }
    [Serializable] class Report { public bool complete;public float elapsed;public int fed,processed;public string status;public BoxRecord[] boxes;public int[] terminalCounts; }
    [Serializable] class BoxRecord { public string id,initialFace,judgedFace,read,region,destination,reason,state; public int rotations;public Vector3 position; }
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12,160,420,185),GUI.skin.box);
        GUILayout.Label("Bonguri 통합 시운전");GUILayout.Label(status);
        GUILayout.Label("투입 "+(feeder==null?0:feeder.FedCount)+" | 적재 완료 "+(cell==null?0:cell.Processed));
        GUI.enabled=!testing&&cell!=null&&!cell.IsRunning&&feeder.InProcess==0;
        if(GUILayout.Button("윗면 상자 3개 시험"))RunSmoke();
        if(GUILayout.Button("측면 상자 1개 · 실제 로봇 교정"))RunSmoke(true);
        GUI.enabled=true;
        if(GUILayout.Button("사이클 정지"))cell.RequestCycleStop();
        GUILayout.Label("A/B/C 끝 정지 · 최종 적재 연결은 별도 점검");
        GUILayout.EndArea();
    }
}
