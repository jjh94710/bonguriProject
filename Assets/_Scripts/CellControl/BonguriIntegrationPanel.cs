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
    Camera demoCamera;
    public enum PickupDirection { X, Y, Z }
    int pickupTerminal = 2;
    BoxInfo pickupTestBox;
    BoxAttacher pickupTestLoader;
    int pickupSerial;
    bool HasActivePickupTest => pickupTestLoader!=null&&(pickupTestLoader.Busy||pickupTestLoader.Attached||
        (pickupTestBox!=null&&!pickupTestLoader.PlacedBoxes.Contains(pickupTestBox.GetComponent<BoxCollider>())));
    public static Quaternion PickupRotation(PickupDirection direction) => direction == PickupDirection.X
        ? Quaternion.Euler(0,90,0) : direction == PickupDirection.Y ? Quaternion.Euler(90,0,0) : Quaternion.identity;
    public void RunDirectionalPickup(PickupDirection direction)
    {
        if(!Application.isPlaying||testing||cell==null||cell.IsRunning||feeder==null||feeder.InProcess>0)return;
        if(pickupTestLoader!=null&&(pickupTestLoader.Busy||pickupTestLoader.Attached))return;
        if(pickupTestBox!=null && (pickupTestLoader==null||!pickupTestLoader.PlacedBoxes.Contains(pickupTestBox.GetComponent<BoxCollider>())))
        {status="남은 시험 상자를 먼저 정리하세요.";return;}
        string terminal=((char)('A'+pickupTerminal)).ToString();
        var loader=FindObjectsByType<BoxAttacher>(FindObjectsSortMode.None).SingleOrDefault(l=>l.name=="fr5_suction"+terminal);
        var line=terminalLines.FirstOrDefault(l=>l!=null&&l.name=="Terminal"+terminal);
        if(loader==null||line==null||feeder.ParcelPrefab==null||!loader.IsReady||loader.Busy||loader.Attached||line.QueueCount>0)
        {status=terminal+" 로봇 준비 상태와 터미널 점유를 확인하세요.";return;}
        var box=Instantiate(feeder.ParcelPrefab,new Vector3(100,100,100),PickupRotation(direction));
        var body=box.GetComponent<Rigidbody>();
        if(body==null){Destroy(box.gameObject);status="시험 상자의 Rigidbody가 없습니다.";return;}
        body.isKinematic=true;
        if(!line.UsesSurfacePath||!line.ValidateSurfacePath(out _)||!line.TryGetSurfacePose(box,PickupRotation(direction),line.PathLength,out var position,out var rotation,out string error))
        {Destroy(box.gameObject);status="최종 벨트 표면 경로를 확인하세요.";return;}
        box.boxId="PICKUP-"+terminal+"-"+direction+"-"+(++pickupSerial);box.name=box.boxId;
        body.position=position;body.rotation=rotation;box.transform.SetPositionAndRotation(position,rotation);
        Physics.SyncTransforms();
        pickupTestBox=box;pickupTestLoader=loader;
        if(!loader.TryLoadBox(box.GetComponent<BoxCollider>(),out string failure))
        {body.isKinematic=true;status=terminal+" / "+direction+" 생성 · 집기 접수 실패: "+failure;return;}
        status=terminal+" / "+direction+" 방향 1개 · 직접 집기 시험 (통합 집계 제외)";
    }
    void ClearPickupTestBox()
    {
        if(pickupTestBox==null)return;
        if(pickupTestLoader!=null&&(pickupTestLoader.Busy||pickupTestLoader.Attached))return;
        if(pickupTestLoader!=null&&pickupTestLoader.PlacedBoxes.Contains(pickupTestBox.GetComponent<BoxCollider>()))return;
        if(pickupTestLoader!=null&&pickupTestLoader.targetBox==pickupTestBox.GetComponent<BoxCollider>())pickupTestLoader.targetBox=null;
        Destroy(pickupTestBox.gameObject);pickupTestBox=null;status="실패한 시험 상자 정리 완료";
    }
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
        if (testing || HasActivePickupTest || cell == null || cell.IsRunning || feeder == null || feeder.InProcess > 0) return;
        StartCoroutine(Smoke(side));
    }
    public void RunRandomDemo()
    {
        if (testing || HasActivePickupTest || cell == null || cell.IsRunning || feeder == null || feeder.InProcess > 0) return;
        ShowDemoCamera();
        StartCoroutine(Smoke(false, true));
    }
    void ShowDemoCamera()
    {
        if (demoCamera == null)
        {
            demoCamera = new GameObject("Runtime sorting demonstration camera").AddComponent<Camera>();
            demoCamera.transform.SetParent(transform);
            var main = Camera.main;
            if (main != null) demoCamera.CopyFrom(main);
            demoCamera.enabled = true;
            demoCamera.targetTexture = null;
            demoCamera.rect = new Rect(0, 0, 1, 1);
            demoCamera.depth = 100;
            demoCamera.clearFlags = CameraClearFlags.Skybox;
            demoCamera.cullingMask = ~0;
            demoCamera.orthographic = true;
            demoCamera.orthographicSize = 3.2f;
            demoCamera.nearClipPlane = .05f;
            demoCamera.farClipPlane = 100;
            demoCamera.transform.position = new Vector3(-.6f, 2.8f, 2.15f);
            demoCamera.transform.rotation = Quaternion.Euler(90, 0, 0);
        }
        demoCamera.enabled = true;
    }
    IEnumerator Smoke(bool side, bool randomDemo = false)
    {
        testing=true;started=Time.time;events.Clear();
        reportFolder="results/bonguri_integration/"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+(randomDemo?"_random":side?"_side":"_top");
        Directory.CreateDirectory(reportFolder);
        var s=cell.Settings;
        float top=s.faceTop, sides=s.faceSide, bottom=s.faceBottom, interval=s.feedInterval;
        s.faceTop=side?0:1;s.faceSide=side?.25f:0;s.faceBottom=0;s.feedInterval=3;
        UnityEngine.Random.InitState(randomDemo ? Environment.TickCount : 20261103);
        status=randomDemo?"무작위 지역 송장 3개 · 실제 영상 판독·분류":side?"측면 1개 · 실제 교정 로봇 시험":"윗면 3개 · 투입→ONNX→OCR→분류 시험";
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
        var matrix = GUI.matrix;
        float scale = Mathf.Max(.5f, Screen.height / 900f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUILayout.BeginArea(new Rect(12,12,440,590),GUI.skin.box);
        GUILayout.Label("Bonguri 통합 시운전");GUILayout.Label(status);
        GUILayout.Label("투입 "+(feeder==null?0:feeder.FedCount)+" | 적재 완료 "+(cell==null?0:cell.Processed));
        bool canRun=!testing&&cell!=null&&!cell.IsRunning&&feeder!=null&&feeder.InProcess==0;
        GUI.enabled=canRun&&!HasActivePickupTest;
        if(GUILayout.Button("무작위 송장 3개 · 분류 시연"))RunRandomDemo();
        if(GUILayout.Button("윗면 상자 3개 시험"))RunSmoke();
        if(GUILayout.Button("측면 상자 1개 · 실제 로봇 교정"))RunSmoke(true);
        GUI.enabled=canRun;
        GUILayout.Label("방향별 직접 집기 · 5×6cm 흡착면의 법선 방향");
        pickupTerminal=GUILayout.Toolbar(pickupTerminal,new[]{"A 로봇","B 로봇","C 로봇"});
        GUI.enabled=GUI.enabled&&(pickupTestLoader==null||(!pickupTestLoader.Busy&&!pickupTestLoader.Attached));
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("X 방향 1개"))RunDirectionalPickup(PickupDirection.X);
        if(GUILayout.Button("Y 방향 1개"))RunDirectionalPickup(PickupDirection.Y);
        if(GUILayout.Button("Z 방향 1개"))RunDirectionalPickup(PickupDirection.Z);
        GUILayout.EndHorizontal();
        if(GUILayout.Button("실패한 시험 상자 정리"))ClearPickupTestBox();
        GUI.enabled=true;
        if(pickupTestLoader!=null)GUILayout.Label("집기 상태: "+pickupTestLoader.Status);
        if(GUILayout.Button("사이클 정지"))cell.RequestCycleStop();
        if(GUILayout.Button("시연 카메라 켜기 / 끄기"))
        { if(demoCamera==null)ShowDemoCamera();else demoCamera.enabled=!demoCamera.enabled; }
        foreach (var box in FindObjectsByType<BoxInfo>(FindObjectsSortMode.None).Where(b=>!string.IsNullOrEmpty(b.boxId)).OrderBy(b=>b.boxId))
        {
            GUILayout.Label(box.boxId+" | 인쇄 "+box.labelNumber+" "+box.labelRegion);
            GUILayout.Label("판독 "+box.readText+" "+box.readRegion+" → "+box.destination+" | "+box.reason);
        }
        GUILayout.Label("A/B/C 끝 정지 · 최종 적재 연결은 별도 점검");
        GUILayout.EndArea();
        if (demoCamera != null && demoCamera.enabled)
        {
            foreach(var box in FindObjectsByType<BoxInfo>(FindObjectsSortMode.None).Where(b=>!string.IsNullOrEmpty(b.boxId)))
            {
                Vector3 p=demoCamera.WorldToScreenPoint(box.transform.position);
                if(p.z>0)GUI.Box(new Rect(p.x/scale-55,(Screen.height-p.y)/scale-32,110,26),box.boxId+" → "+box.destination);
            }
        }
        GUI.matrix = matrix;
    }
}
