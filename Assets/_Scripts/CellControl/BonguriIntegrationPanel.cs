using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

//  ============================================================
//  BonguriIntegrationPanel
//  통합 시운전 패널 (Play 중 화면 왼쪽 위). 버튼 하나로 짧은 시험 운전을 돌리고 결과를 파일로 남긴다.
//      - 무작위 송장 3개 · 분류 시연 / 윗면 상자 3개 시험 / 측면 상자 1개 · 실제 로봇 교정, 세 가지 시험이 있다.
//      - 시험 동안 CellController 로그([Cell] · [Feeder] · [Conveyor] · [Pusher], 오류 · 예외)를 모은다.
//      - 1초마다 상자별 판정 · 판독 · 목적지 · 상태를 results/bonguri_integration/<시각>_<종류>/report.json과 events.log에 쓴다.
//      - 시연 카메라: 공정을 위에서 내려다보는 정사영 카메라를 만들고, 화면 위 상자마다 "상자 ID → 목적지"를 띄운다.
//
//  [사용] 지훈님 도구(BonguriIntegrationSetup)가 BonguriProcess에 붙이고 Cell · Feeder · Terminal Lines를 연결한다.
//         같은 도구의 에디터 명령(run-random · run-side · 그 밖 = 윗면)도 RunRandomDemo() · RunSmoke()를 부른다.
//  [!] 터미널 도착은 확인 지점일 뿐 적재 완료가 아니다. 시험은 A · B · C 끝에 상자가 도착하면(또는 제한 시간이 지나면) 끝난다.
//  [!] 시험 동안 CellSettings 실행 중 사본의 투입 면 비율 · 투입 간격을 바꿨다가, 끝나면 되돌린다.
//  [!] 기록 폴더는 프로젝트 폴더 아래 results/ 다 (Assets 밖).
//
//  [설계서와의 차이]
//      * 설계서에 없는 클래스다. 지훈님이 통합 시운전용으로 더했다 (11/03). 설계서의 작업자 화면(UI)과는 별개다.
//  ============================================================
public sealed class BonguriIntegrationPanel : MonoBehaviour
{
    //  ---------------- 연결 ----------------
    [Header("연결")]
    [Tooltip("공정 제어. 시험 운전 시작 · 사이클 정지 · 집계를 쓴다")]
    public CellController cell;

    [Tooltip("투입기. 투입 수 · 처리 중 상자 수를 본다")]
    public BoxFeeder feeder;

    [Tooltip("터미널 A · B · C의 마지막 벨트 구간. 끝에 도착한 상자 수(QueueCount)로 시험 끝을 정한다")]
    public ConveyorLine[] terminalLines;

    //  ---------------- 상태 ----------------
    [Header("상태")]
    [Tooltip("지금 시험 상태 문구 (화면에도 보인다)")]
    public string status = "통합 연결 준비 · 상자 3개 시험";


    //  ============================================================
    //  안쪽 값
    //  ============================================================
    bool testing;                                           //  시험 중인지 (버튼 막기, 로그 모으기)
    float started;                                          //  시험 시작 시각
    string reportFolder;                                    //  이번 시험 기록 폴더
    Camera demoCamera;                                      //  시연 카메라 (처음 켤 때 만든다)
    readonly System.Collections.Generic.List<string> events = new System.Collections.Generic.List<string>();    //  모은 로그 (events.log)


    //  ============================================================
    //  시작 · 끝
    //  ============================================================
    // 켜질 때 Unity 로그를 받기 시작한다.
    void OnEnable()
    {
        Application.logMessageReceived += Log;
    }

    // 꺼질 때 로그 받기를 멈춘다.
    void OnDisable()
    {
        Application.logMessageReceived -= Log;
    }

    // 시험 중이면 공정 로그와 오류 · 예외만 "시각 종류 내용"으로 모은다.
    void Log(string message, string stack, LogType type)
    {
        if (!testing ||
            !(message.StartsWith("[Cell") || message.StartsWith("[Feeder") || message.StartsWith("[Conveyor") || message.StartsWith("[Pusher") ||
              type == LogType.Exception || type == LogType.Error)) { return; }
        events.Add(Time.time.ToString("F3") + " " + type + " " + message);
    }


    //  ============================================================
    //  시험 명령
    //  ============================================================
    // 시험 운전을 시작한다. side = false: 윗면 상자 3개 (투입 → ONNX → OCR → 분류), true: 측면 상자 1개 (실제 교정 로봇).
    /// [호출] OnGUI() 버튼, BonguriIntegrationSetup 에디터 명령
    /// [!] 시험 중 · 운전 중 · 처리 중 상자가 있으면 아무것도 하지 않는다.
    /// [주의] feeder가 비어 있으면 feeder.InProcess에서 오류가 난다 (RunRandomDemo()와 달리 feeder를 확인하지 않는다)
    public void RunSmoke(bool side = false)
    {
        if (testing || cell == null || cell.IsRunning || feeder.InProcess > 0) { return; }
        StartCoroutine(Smoke(side));
    }

    // 무작위 송장 상자 3개로 분류 시연을 시작한다 (실제 영상 판독 · 분류). 시연 카메라도 켠다.
    /// [호출] OnGUI() 버튼, BonguriIntegrationSetup 에디터 명령 (run-random)
    public void RunRandomDemo()
    {
        if (testing || cell == null || cell.IsRunning || feeder == null || feeder.InProcess > 0) { return; }
        ShowDemoCamera();
        StartCoroutine(Smoke(false, true));
    }

    // 시연 카메라를 켠다. 없으면 만든다: 메인 카메라 설정을 복사한 뒤, 위에서 곧게 내려다보는 정사영(크기 3.2)으로 바꾼다.
    /// 화면 전체 · 가장 앞(depth 100)에 그린다. 위치 (-0.6, 2.8, 2.15), 아래를 본다 (X 90°).
    void ShowDemoCamera()
    {
        if (demoCamera == null)
        {
            demoCamera = new GameObject("Runtime sorting demonstration camera").AddComponent<Camera>();
            demoCamera.transform.SetParent(transform);
            var main = Camera.main;
            if (main != null) { demoCamera.CopyFrom(main); }
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


    //  ============================================================
    //  시험 실행
    //  ============================================================
    // 시험 한 번: 기록 준비 → 투입 설정 바꾸기 → 운전 → 1초마다 기록 → 끝나면 사이클 정지 · 설정 되돌리기 · 마지막 기록.
    /// 끝 조건: 투입 수와 터미널 끝 도착 수가 목표(윗면 3 · 측면 1)에 닿거나, 운전이 멈추거나(시작 2초 뒤부터), 제한 시간(윗면 45초 · 측면 100초).
    /// 투입 면 비율: 윗면 시험 = 윗면 100%, 측면 시험 = 옆면 4개 25%씩. 투입 간격 3초.
    /// 난수 시드: 무작위 시연 = 매번 다르게, 그 밖 = 20261103 고정 (같은 상자가 다시 나온다).
    IEnumerator Smoke(bool side, bool randomDemo = false)
    {
        //  1) 기록 준비
        testing = true;
        started = Time.time;
        events.Clear();
        reportFolder = "results/bonguri_integration/" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (randomDemo ? "_random" : side ? "_side" : "_top");
        Directory.CreateDirectory(reportFolder);

        //  2) 투입 설정을 시험용으로 바꾼다 (원래 값은 기억해 두었다가 끝에서 되돌린다)
        var settings = cell.Settings;
        float top = settings.faceTop;
        float sides = settings.faceSide;
        float bottom = settings.faceBottom;
        float interval = settings.feedInterval;
        settings.faceTop = side ? 0 : 1;
        settings.faceSide = side ? .25f : 0;
        settings.faceBottom = 0;
        settings.feedInterval = 3;
        UnityEngine.Random.InitState(randomDemo ? Environment.TickCount : 20261103);

        //  3) 운전 시작
        status = randomDemo ? "무작위 지역 송장 3개 · 실제 영상 판독·분류" : side ? "측면 1개 · 실제 교정 로봇 시험" : "윗면 3개 · 투입→ONNX→OCR→분류 시험";
        cell.Run(side ? 1 : 3);

        //  4) 끝날 때까지 1초마다 기록한다
        while (Time.time - started < (side ? 100 : 45))
        {
            WriteReport(false);
            if (feeder.FedCount >= (side ? 1 : 3) && terminalLines.Where(l => l != null).Sum(l => l.QueueCount) >= (side ? 1 : 3)) { break; }
            if (!cell.IsRunning && Time.time - started > 2) { break; }
            yield return new WaitForSeconds(1);
        }

        //  5) 사이클 정지 · 설정 되돌리기 · 마지막 기록
        cell.RequestCycleStop();
        settings.faceTop = top;
        settings.faceSide = sides;
        settings.faceBottom = bottom;
        settings.feedInterval = interval;
        status = "시험 기록 저장 · 터미널 도착은 적재 완료와 별도";
        WriteReport(true);
        testing = false;
    }


    //  ============================================================
    //  기록
    //  ============================================================
    // 상자별 기록(ID 있는 상자, ID 순)과 집계를 report.json에, 모은 로그를 events.log에 쓴다 (매번 덮어쓴다).
    /// complete = 시험이 끝난 뒤의 마지막 기록인지
    void WriteReport(bool complete)
    {
        var boxes = FindObjectsByType<BoxInfo>(FindObjectsSortMode.None).Where(b => !string.IsNullOrEmpty(b.boxId)).OrderBy(b => b.boxId).Select(b => new BoxRecord
        {
            id = b.boxId,
            initialFace = b.initialFace.ToString(),
            judgedFace = b.judgedFace.ToString(),
            read = b.readText,
            region = b.readRegion,
            destination = b.destination.ToString(),
            reason = b.reason.ToString(),
            state = b.state.ToString(),
            rotations = b.rotationCount,
            position = b.transform.position
        }).ToArray();
        var report = new Report
        {
            complete = complete,
            elapsed = Time.time - started,
            fed = feeder.FedCount,
            processed = cell.Processed,
            status = status,
            boxes = boxes,
            terminalCounts = terminalLines.Select(l => l == null ? 0 : l.QueueCount).ToArray()
        };
        File.WriteAllText(reportFolder + "/report.json", JsonUtility.ToJson(report, true));
        File.WriteAllLines(reportFolder + "/events.log", events);
    }

    //  report.json 내용 (칸 이름이 그대로 JSON 키가 된다. 바꾸지 않는다)
    [Serializable] class Report 
        { public bool complete; public float elapsed; public int fed, processed; public string status; public BoxRecord[] boxes; public int[] terminalCounts; }
    [Serializable] class BoxRecord 
        { public string id, initialFace, judgedFace, read, region, destination, reason, state; public int rotations; public Vector3 position; }


    //  ============================================================
    //  화면
    //  ============================================================
    // 시운전 패널을 그린다 (화면 높이 900 기준으로 확대 · 축소). 시연 카메라가 켜져 있으면 상자 위에 "상자 ID → 목적지"를 띄운다.
    /// 시험 버튼 3개는 시험 중 · 운전 중 · 처리 중 상자가 있으면 막는다. 사이클 정지 · 카메라 버튼은 항상 누를 수 있다.
    /// [주의] feeder가 비어 있으면 버튼 막기 줄(feeder.InProcess)에서 매 프레임 오류가 난다
    void OnGUI()
    {
        var matrix = GUI.matrix;
        float scale = Mathf.Max(.5f, Screen.height / 900f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));

        //  1) 패널: 상태 · 투입 수 · 처리 수 · 버튼
        GUILayout.BeginArea(new Rect(12, 12, 440, 420), GUI.skin.box);
        GUILayout.Label("Bonguri 통합 시운전");
        GUILayout.Label(status);
        GUILayout.Label("투입 " + (feeder == null ? 0 : feeder.FedCount) + " | 적재 완료 " + (cell == null ? 0 : cell.Processed));
        GUI.enabled = !testing && cell != null && !cell.IsRunning && feeder.InProcess == 0;
        if (GUILayout.Button("무작위 송장 3개 · 분류 시연")) { RunRandomDemo(); }
        if (GUILayout.Button("윗면 상자 3개 시험")) { RunSmoke(); }
        if (GUILayout.Button("측면 상자 1개 · 실제 로봇 교정")) { RunSmoke(true); }
        GUI.enabled = true;
        if (GUILayout.Button("사이클 정지")) { cell.RequestCycleStop(); }
        if (GUILayout.Button("시연 카메라 켜기 / 끄기"))
        {
            if (demoCamera == null) { ShowDemoCamera(); }
            else { demoCamera.enabled = !demoCamera.enabled; }
        }

        //  2) 상자 목록: 인쇄된 송장 · 판독 결과 → 목적지 | 예외 사유
        foreach (var box in FindObjectsByType<BoxInfo>(FindObjectsSortMode.None).Where(b => !string.IsNullOrEmpty(b.boxId)).OrderBy(b => b.boxId))
        {
            GUILayout.Label(box.boxId + " | 인쇄 " + box.labelNumber + " " + box.labelRegion);
            GUILayout.Label("판독 " + box.readText + " " + box.readRegion + " → " + box.destination + " | " + box.reason);
        }
        GUILayout.Label("A/B/C 끝 정지 · 최종 적재 연결은 별도 점검");
        GUILayout.EndArea();

        //  3) 시연 카메라 화면 위 상자 이름표 (카메라 앞에 있는 상자만)
        if (demoCamera != null && demoCamera.enabled)
        {
            foreach (var box in FindObjectsByType<BoxInfo>(FindObjectsSortMode.None).Where(b => !string.IsNullOrEmpty(b.boxId)))
            {
                Vector3 screen = demoCamera.WorldToScreenPoint(box.transform.position);
                if (screen.z > 0) { GUI.Box(new Rect(screen.x / scale - 55, (Screen.height - screen.y) / scale - 32, 110, 26), box.boxId + " → " + box.destination); }
            }
        }
        GUI.matrix = matrix;
    }
}
