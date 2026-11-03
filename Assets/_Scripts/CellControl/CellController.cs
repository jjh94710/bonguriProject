using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//  ============================================================
//  CellController
//  전체 공정을 순서대로 진행하는 총괄 스크립트.                           
//      - 교정 스테이션과 판독 스테이션을 코루틴 2개로 동시에 돌린다.
//      - 상자가 터미널 컨테이너나 NG 토트에 들어가면 Finish()로 최종 상태를 정하고 집계한다.
//      - 이상 상황은 RaiseAlarm()로 알리고, 같은 경고가 연속되면 알람으로 멈춘다.
//
                    ///// : TODO : 현재는 골격만 세워진 단계
//  [골격 단계] 설정 복사, 설비 상태, 운전 명령, 제어 방식, 알람, 집계까지 들어 있다.
//             스테이션 동작과 다른 스크립트 연결은 그 스크립트를 만들 때 하나씩 채운다 ([다음 단계] 표시).
//  
//  [사용]      공정 씬에 빈 오브젝트 "Cell"을 만들고 붙인다. 다른 스크립트는 Inspector 칸으로 이 컴포넌트를 받는다.
//
//  [설계서와의 차이]
//      * 설계서의 - 칸은 [SerializeField]로 두었다. Inspector에는 보이고, 다른 스크립트는 바꾸지 못한다.
//      * IsRunning 속성을 추가했다. 운전 제어 패널이 버튼을 켜고 끌 때 쓴다.
//      * ResetAlarm()은 알람만 풀고, 운전은 Run()으로 다시 시작한다. 설계서의 "운전 재개"를 작업자가 시작 버튼을 다시 누르는 것으로 해석했다.
//      * 분류 성공 · 실패는 송장 정답(labelNumber, labelRegion)으로 찾은 터미널과 실제 목적지를 비교해 센다.
//      * Run(n)은 feeder.StartFeeding(n)으로 상자를 넣는다 (설계서: FeedOne()을 n번).
//      * Run(n)이 끝나는 조건에 "다 넣었고 처리 중인 상자 없음"을 더했다. 분류 구간이 없는 테스트 씬에서는 상자가 출구에서 지워져 처리 수가 늘지 않기 때문.
//      * 처리 중인 상자 수는 feeder.InProcess로 센다 (설계서: 투입 수 - 처리 수).
//      * 측정할 때 상자만 잠깐 카메라 레이어로 옮겨 찍는다. 뒤에 줄 선 상자 · 벨트 · 로봇은 찍히지 않는다 (설계서: 측면 카메라 위치를 씬 작업에서 정함).
//      * 행동 선택(ChooseFace)은 위로 올릴 면을 돌려주고, Top이면 완료로 본다 (설계서: 완료 + 올릴 면 5개).
//      * 카메라 5대가 없으면 운전을 시작하지 않는다 (Check). 운전 중에 없어지면 JudgeFail로 표시한다.
//      * 교정 로봇(correction)이 비어 있으면 상자를 그 자리에서 바로 돌린다 (RotateInPlace, 로봇 없는 테스트 씬용).
//      * 교정 로봇이 실패하면(LastSuccess false) 경고를 올리고 더 돌리지 않는다. CorrectionFail 표시는 CorrectionSequence.CorrectAt()이 한다.
//      * LabelReader가 비어 있으면 상자를 ReadFail로 보낸다. 테스트 씬은 placeholderRead를 켜서 송장 정답으로 분류를 시험한다.
//      * 제어 방식(FR-12)은 송장 면 판정만 다르다. 기본 제어 = 색 비율 판정, 학습 정책 = 송장 유무 ONNX 판정(LabelPresenceDetector, 지도학습).
//        회전은 두 방식 모두 규칙으로 정한다 (설계서: CorrectionAgent 강화학습. 11/03 팀 원칙으로 지훈님 지도학습 판정을 따른다).
//      * 시작 제어 방식은 startMode 칸으로 정한다. 학습 정책인데 모델이 없으면 기본 제어로 시작한다.
//  ============================================================
public class CellController : MonoBehaviour
{
    //  ---------------- 설정 ----------------    (NFR-09)
    [Header("설정")]
    [Tooltip("공정 파라미터 에셋 (CellSettings). 시작할 때 복사본을 만들어 Settings로 나눠 준다")]
    [SerializeField] CellSettings settings;

    [Tooltip("같은 경고가 이 횟수만큼 연속되면 알람으로 멈춘다 (default: 3)")]
    [Min(1)][SerializeField] int maxConsecutiveErrors = 3;

    [Tooltip("교정 스테이션 제한 시간(초). 도착부터 이 시간이 지나면 더 돌리지 않고 판독 구간으로 보낸다 (default: 10)")]
    [Min(1f)][SerializeField] float correctTimeLimit = 10f;

    [Tooltip("시작할 때 제어 방식. Policy인데 Face Detector에 모델이 없으면 Basic으로 시작한다 (default: Policy)")]
    [SerializeField] ControlMode startMode = ControlMode.Policy;

    //  ---------------- 연결 ----------------
    [Header("연결")]
    [Tooltip("교정 스테이션 구간 (정지 위치가 있는 ConveyorLine)")]
    [SerializeField] ConveyorLine correctLine;

    [Tooltip("판독 스테이션 구간 (정지 위치가 있는 ConveyorLine)")]
    [SerializeField] ConveyorLine readLine;

    [Tooltip("상자 투입 (BoxFeeder)")]
    [SerializeField] BoxFeeder feeder;

    [Tooltip("교정 스테이션 카메라 5대(FaceCameraRig). 정지 위치에 선 상자의 송장 위치를 잰다")]
    [SerializeField] FaceCameraRig faceRig;
    [Tooltip("학습 정책용 송장 유무 ONNX 판정 (LabelPresenceDetector). 비우면 기본 제어(색 비율 판정)만 쓴다")]
    [SerializeField] LabelPresenceDetector faceDetector;
    [Tooltip("송장 판독 (LabelReader). 비어 있으면 상자를 ReadFail로 보낸다")]
    [SerializeField] LabelReader reader;
    [Tooltip("LabelReader가 없는 테스트 씬에서만 켠다. 켜면 1초 뒤 송장 정답으로 목적지를 정한다 (default: 꺼짐)")]
    [SerializeField] bool placeholderRead = false;

    [Tooltip("(선택) 교정 로봇암 (CorrectionSequence). 비우면 상자를 그 자리에서 바로 돌린다 (자리 표시)")]
    [SerializeField] CorrectionSequence correction;

    [System.Serializable]
    public sealed class TerminalStation
    {
        public Destination destination;
        public ConveyorLine line;
        public BoxAttacher loader;
        [Tooltip("로봇 루트에서 집을 상자 중심까지 허용 거리(m). 벨트 경로 미연결 상태의 먼 상자는 집지 않는다.")]
        [Min(.1f)] public float maxPickupDistance = 1f;
    }
    [Header("터미널 적재 (A / B / C)")]
    [Tooltip("실제 공정은 A/B/C 세 구간을 연결한다. 비워 두면 기존 분류 전용 시험처럼 적재를 실행하지 않는다.")]
    [SerializeField] TerminalStation[] terminalStations = new TerminalStation[0];
    [Min(1f)][SerializeField] float loadingTimeLimit = 180f;
    readonly HashSet<BoxInfo> blockedLoads = new HashSet<BoxInfo>();
    readonly HashSet<BoxInfo> waitingTransfers = new HashSet<BoxInfo>();

                        ///// : TODO : 
    //  [스크립트 구현 마다] 아래 칸은 해당 스크립트를 만들 때 하나씩 연다.
    //      loaders (BoxAttacher[3])                 터미널 끝 흡착 로봇, 봉현님 (Check()와 알람 정지용)
    //      log (ProductionLog)                      세션 로그
    //      제한 시간: supplyTimeout 30, readTimeout 2, inferenceTimeout 1 (초)


    //  ============================================================
    //  상태 (대시보드 · 다른 스크립트가 읽는 값)
    //  ============================================================
    /// 실행 중 설정 복사본. 다른 스크립트는 이 값을 읽는다 (원본 에셋은 바뀌지 않는다)
    public CellSettings Settings { get; private set; }

    /// 설비 상태 (가동 · 대기 · 정지)    (FR-10)
    public CellState State { get; private set; } = CellState.Stopped;

    /// 지금 제어 방식, 다음 상자부터 쓸 제어 방식    (FR-12)
    public ControlMode Mode { get; private set; } = ControlMode.Basic;
    public ControlMode PendingMode { get; private set; } = ControlMode.Basic;

    /// 학습 정책을 쓸 수 있는지 (Face Detector 칸에 송장 유무 ONNX 모델이 연결되어 있으면 true).
    /// 모델 규약까지는 SetMode()와 Check()가 ValidateConfiguration()으로 확인한다.
    public bool HasPolicy => faceDetector != null && faceDetector.HasModel;

    /// 처리 수 = 분류 성공 + 분류 실패 + 예외. 회전 횟수 합
    public int Processed { get; private set; }
    public int Success { get; private set; }
    public int Fail { get; private set; }
    public int Exceptions { get; private set; }
    public int Corrections { get; private set; }

    /// 터미널 A · B · C별 분류 수, 예외 사유별 건수 (ExceptionReason 순서)    (FR-08, FR-09)
    public int[] TerminalCounts { get; private set; } = new int[3];
    public int[] ExceptionsByReason { get; private set; } = new int[System.Enum.GetValues(typeof(ExceptionReason)).Length];

                        ///// : TODO : 
    /// [ConveyorLine 구현 후] StationWaitSeconds(교정 스테이션 대기 시간)는 ConveyorLine 연결 후 추가한다.
    /// 시간당 판별량 = 최근 60초 처리 수 × 60, 예외율 (0 ~ 1)    (FR-10)
    public float ThroughputPerHour => finishTimes.Count * 60f;
    public float ExceptionRate => Processed > 0 ? (float)Exceptions / Processed : 0f;

    /// 운전 중인지 (Run()을 부른 뒤 끝나기 전까지)
    /// [!] RunRoutine()이 시작하자마자 true로 바꾼다. 스테이션 코루틴이 바로 이 값을 보고 돌기 때문.
    public bool IsRunning { get; private set; }

    /// 이상 상황 알림 (종류, 상자 ID) --> AlarmUI가 구독
    public event System.Action<string, string> OnAlarm;

    bool stopRequested;                                     //  사이클 정지를 눌렀는지
    bool alarmActive;                                       //  알람으로 멈춘 상태인지 (ResetAlarm()으로 해제)
    string lastAlarmType;                                   //  마지막 경고 종류 (연속 횟수 세기용)
    int consecutiveErrors;                                  //  같은 경고가 연속된 횟수
    readonly Queue<float> finishTimes = new Queue<float>(); //  최근 60초 안의 처리 시각
    int testCount;                                          //  테스트 상자 번호


    //  ============================================================
    //  시작
    //  ============================================================
    // 설정 에셋의 복사본을 만든다.
    /// 다른 스크립트의 Start()보다 먼저 준비되도록 Awake()에서 한다.
    /// 복사본이라 Play 중 운전 제어 패널에서 값을 바꿔도 원본 에셋 파일은 그대로 남는다.
    void Awake()
    {
        if (settings == null)
        {
            Debug.LogWarning("[Cell] Settings 칸이 비어 있습니다. CellSettings 에셋을 넣으세요.", this);
            return;
        }
        Settings = Instantiate(settings);
        Settings.name = $"{settings.name} (실행 중)";
    }

    // 시작 제어 방식을 정한다. 학습 정책인데 모델이 없으면 기본 제어로 시작한다 (FR-12 "모델 파일 없음").
    void Start()
    {
        ControlMode mode = startMode == ControlMode.Policy && !HasPolicy ? ControlMode.Basic : startMode;
        Mode = mode;
        PendingMode = mode;
        if (mode != startMode)
            { Debug.Log("[Cell] 학습 정책 모델이 없어 기본 제어로 시작합니다.", this); }
    }

    // 60초가 지난 처리 시각을 버린다 (시간당 판별량 계산용).
    void Update()
    {
        while (finishTimes.Count > 0 && Time.time - finishTimes.Peek() > 60f)
            { finishTimes.Dequeue(); }
    }


    //  ============================================================
    //  운전 명령 (운전 제어 패널 · 테스트 메뉴가 부른다)
    //  ============================================================
    // 운전 시작. count = 0이면 연속 운전, 1 이상이면 상자 count개만 처리한다.
    /// [주의] 알람 중이거나 필수 칸이 비어 있으면 시작하지 않는다.
    public void Run(int count)
    {
        if (IsRunning)
        {
            Debug.Log("[Cell] 이미 운전 중입니다.", this);
            return;
        }
        if (alarmActive)
        {
            Debug.LogWarning("[Cell] 알람 중입니다. 먼저 ResetAlarm()으로 해제하세요.", this);
            return;
        }
        if (!Check()) { return; }

        StartCoroutine(RunRoutine(Mathf.Max(0, count)));     //  음수는 연속 운전(0)으로 본다
    }

    // 사이클 정지. 투입을 멈추고, 처리 중인 상자를 끝낸 뒤 정지한다.
    public void RequestCycleStop()
    {
        if (!IsRunning || stopRequested) { return; }

        stopRequested = true;
        feeder.StopFeeding();
        Debug.Log("[Cell] 사이클 정지 요청 | 처리 중인 상자가 끝나면 멈춤", this);
    }

    // 제어 방식을 바꾼다. 지금 상자가 아니라 다음 상자부터 적용한다.
    /// 학습 정책을 골랐는데 모델이 없으면 바꾸지 않고 false를 돌려준다 (FR-12 "모델 파일 없음").
    public bool SetMode(ControlMode mode)
    {
        if (mode == ControlMode.Policy && !HasPolicy)
        {
            Debug.LogWarning("[Cell] 모델 파일 없음: 학습 정책으로 바꿀 수 없습니다.", this);
            return false;
        }
        if (mode == ControlMode.Policy && !faceDetector.ValidateConfiguration(out string modelError))
        {
            Debug.LogWarning("[Cell] ONNX 준비 실패: 학습 정책으로 바꿀 수 없습니다. " + modelError, this);
            return false;
        }
        PendingMode = mode;
        Debug.Log($"[Cell] 제어 방식 예약: {mode} (다음 상자부터)", this);
        return true;
    }


    //  ============================================================
    //  알람
    //  ============================================================
    // 이상 상황을 알리고 이벤트 로그에 남긴다. 다른 스크립트도 부른다 (투입 지연, 분류 실패 등).
    /// 같은 종류가 maxConsecutiveErrors번 연속되면 알람으로 멈춘다. 상자가 분류에 성공하면 연속 횟수는 0으로 돌아간다.
    public void RaiseAlarm(string type, string boxId)
    {
        consecutiveErrors = type == lastAlarmType ? consecutiveErrors + 1 : 1;
        lastAlarmType = type;

        Debug.LogWarning($"[Cell] 경고 | {type} | {boxId} ({consecutiveErrors}회 연속)", this);
        OnAlarm?.Invoke(type, boxId);
        ///// : TODO : 
        //  [다음 단계] log.Event(type, boxId);

        if (consecutiveErrors >= maxConsecutiveErrors && !alarmActive)
        {
            alarmActive = true;
            stopRequested = true;
            State = CellState.Stopped;
            if (feeder != null) { feeder.StopFeeding(); }       // 운전 전 테스트에서도 불릴 수 있어 빈 칸을 확인한다
            if (correction != null) { correction.Stop(); }      // 교정 로봇은 그 자리에서 관절각을 유지한다 (손가락은 그대로)
            StopTerminalLoaders();

                                ///// : TODO : 
            //  [다음 단계] 벨트 · 끝 로봇 3대 정지

            Debug.LogError($"[Cell] 알람 정지 | {type} {consecutiveErrors}회 연속", this);
        }
    }

    // 알람 해제. 작업자 확인이 끝난 뒤 부른다. 운전은 Run()으로 다시 시작한다.
    public void ResetAlarm()
    {
        if (!alarmActive) { return; }

        alarmActive = false;
        consecutiveErrors = 0;
        lastAlarmType = null;
        Debug.Log("[Cell] 알람 해제 | Run()으로 다시 시작", this);
    }


    //  ============================================================
    //  집계
    //  ============================================================
    // 최종 상태를 정하고 집계 · 로그를 남긴다. 집계는 이 함수에서만 한다.
    /// [호출] 상자가 터미널 컨테이너나 NG 토트에 들어갈 때 ToteBase.Add()가 부른다 (터미널은 끝 로봇이 내려놓은 뒤).
    /// [!] 같은 상자를 두 번 넘겨도 한 번만 센다 (이미 최종 상태면 무시).
    public void Finish(BoxInfo box)
    { Finish(box, box != null ? box.destination : Destination.None); }

    public void Finish(BoxInfo box, Destination actualDestination)
    {
        if (box == null || box.IsFinished) { return; }
        if (actualDestination == Destination.None) { return; }

        box.actualDestination = actualDestination;
        box.finishTime = Time.time;
        Processed++;
        Corrections += box.rotationCount;
        finishTimes.Enqueue(Time.time);

        string result;
        if (box.destination == Destination.NG || box.reason != ExceptionReason.None)
        {
            //  1) 예외: NG 토트에 들어간 상자
            box.state = BoxState.Exception;
            Exceptions++;
            ExceptionsByReason[(int)box.reason]++;
            result = $"예외 ({box.reason})";
        }
        else
        {
            //  2) 적재: 터미널 컨테이너에 들어간 상자. 정답 터미널과 같으면 분류 성공
            box.state = BoxState.Loaded;
            int terminal = (int)actualDestination - 1;    //  A 0, B 1, C 2
            if (terminal >= 0 && terminal < TerminalCounts.Length) { TerminalCounts[terminal]++; }

            Destination answer = ExpectedDestination(box);
            if (actualDestination == answer)
            {
                Success++;
                consecutiveErrors = 0;
                result = "분류 성공";
            }
            else
            {
                Fail++;
                result = $"분류 실패 (정답 {answer})";
            }
        }

        Debug.Log($"[Cell] {box.boxId} {result} | 목적지 {box.destination} | 실제 도착 {actualDestination} | 회전 {box.rotationCount}회", this);
                            ///// : TODO : 
        //  [다음 단계] log.Write(box);
    }

    // 정답 목적지: 송장 정답(labelNumber, labelRegion)을 지역 코드 표로 찾은 터미널. 규칙 위반이면 NG.
    /// 분류 성공 · 실패를 셀 때만 쓴다. 공정의 판단은 판독 결과(readText, readRegion)로 한다.
    Destination ExpectedDestination(BoxInfo box)
    {
        if (Settings == null || !box.hasLabel) { return Destination.NG; }
        if (box.labelNumber == null || box.labelNumber.Length != 5) { return Destination.NG; }
        if (!int.TryParse(box.labelNumber.Substring(0, 2), out int code)) { return Destination.NG; }

        int index = System.Array.IndexOf(Settings.regionCodes, code);
        if (index < 0 || index >= Settings.terminals.Length) { return Destination.NG; }
        if (index < Settings.regionNames.Length && box.labelRegion != Settings.regionNames[index]) { return Destination.NG; }

        return Settings.terminals[index];
    }


    //  ============================================================
    //  운전 흐름 (코루틴)
    //  ============================================================
    // 운전 1회. 스테이션 코루틴을 시작하고, 끝날 조건이 될 때까지 기다린다.
    /// 끝나는 조건: 알람, 상자 count개 처리(count > 0), 사이클 정지 후 처리 중인 상자 없음
    IEnumerator RunRoutine(int count)
    {
        IsRunning = true;
        stopRequested = false;
        int target = Processed + count;
        Mode = PendingMode;
        State = CellState.Running;
        Debug.Log($"[Cell] 운전 시작 | {(count > 0 ? $"상자 {count}개" : "연속 운전")} | 제어 방식 {Mode}", this);

        //  투입을 먼저 시작한다. Feeder의 칸이 비어 시작하지 못하면 운전도 멈춘다
        feeder.StartFeeding(count);
        if (!feeder.IsFeeding)
        {
            State = CellState.Stopped;
            IsRunning = false;
            Debug.LogWarning("[Cell] 투입을 시작하지 못해 운전을 멈춥니다. Feeder 경고를 확인하세요.", this);
            yield break;
        }

        Coroutine correctLoop = StartCoroutine(CorrectStationLoop());
        Coroutine readLoop = StartCoroutine(ReadStationLoop());
        var loadingLoops = new List<Coroutine>();
        foreach (var station in terminalStations)
            loadingLoops.Add(StartCoroutine(TerminalStationLoop(station)));

        while (!alarmActive)
        {
            if (count > 0 && Processed >= target) { break; }
            if (count > 0 && !feeder.IsFeeding && InProcessCount() == 0) { break; }
            if (stopRequested && InProcessCount() == 0) { break; }

            State = feeder.IsPaused ? CellState.Waiting : CellState.Running;        // 대기 상자가 많아 투입을 멈추면 대기 (FR-10)
            yield return null;
        }

        //  투입과 스테이션 코루틴을 멈춘다. 멈추고 곧바로 다시 Run()을 눌러도 코루틴이 두 벌로 돌지 않게 한다
        feeder.StopFeeding();
        StopCoroutine(correctLoop);
        StopCoroutine(readLoop);
        foreach (var loop in loadingLoops) StopCoroutine(loop);
        StopTerminalLoaders();
        State = CellState.Stopped;
        IsRunning = false;
        Debug.Log($"[Cell] 운전 끝 | {Summary()}", this);
    }

    // 처리 중인 상자 수 (투입했지만 아직 Finish()되지 않은 상자).
    /// feeder.InProcess를 쓴다. 설계서의 "투입 수 - 처리 수" 대신, 출구에서 지워진 상자도 빠진다.
    int InProcessCount()
    {
        return feeder != null ? feeder.InProcess : 0;
    }

    public bool ValidateTerminalConfiguration(out string error)
    {
        error = null;
        if (terminalStations == null) { error = "Terminal Stations 배열이 없습니다."; return false; }
        var destinations = new HashSet<Destination>();
        var lanes = new HashSet<ConveyorLine>();
        var loaders = new HashSet<BoxAttacher>();
        foreach (var station in terminalStations)
        {
            if (station == null || (int)station.destination < (int)Destination.A || (int)station.destination > (int)Destination.C ||
                !destinations.Add(station.destination) || station.line == null ||
                station.loader == null || !loaders.Add(station.loader))
            { error = "터미널 목적지/정지 구간/적재 로봇이 비었거나 중복입니다."; return false; }
            var end = TerminalEnd(station.line);
            if (end == null || end.StopPoint == null || !lanes.Add(end))
            { error = "터미널 이송 경로가 순환하거나 최종 정지 구간이 비었거나 중복입니다."; return false; }
            var loader = station.loader;
            var tote = loader.toto;
            if (!loader.isActiveAndEnabled || loader.tcp == null || loader.suctionCup == null || loader.armIK == null ||
                tote == null || !tote.isActiveAndEnabled || tote.loader != loader || loader.container != tote.transform ||
                loader.slots == null || tote.slots == null || loader.slots.Length != tote.Capacity || tote.slots.Length != tote.Capacity)
            { error = station.destination + " 적재 로봇/수거함 연결을 확인하세요."; return false; }
            var slots = new HashSet<Transform>();
            for (int i = 0; i < tote.Capacity; i++)
                if (loader.slots[i] == null || loader.slots[i] != tote.slots[i] || !slots.Add(loader.slots[i]) || !loader.slots[i].IsChildOf(tote.transform))
                { error = station.destination + " 수거함 슬롯이 누락되었거나 일치하지 않습니다."; return false; }
        }
        return true;
    }

    IEnumerator TerminalStationLoop(TerminalStation station)
    {
        while (IsRunning && !alarmActive)
        {
            AdvanceTerminalTransfers(station.line);
            // 분기 구간이 아니라 NextLine 연결을 모두 지난 최종 정지점에서만 인계한다.
            var loadingLine = TerminalEnd(station.line);
            if (loadingLine == null) { yield return null; continue; }
            var box = loadingLine.CurrentBox;
            if (box == null || blockedLoads.Contains(box) || box.IsFinished || !station.loader.IsReady || station.loader.Busy || station.loader.Attached)
            { yield return null; continue; }
            if (Vector3.Distance(station.loader.transform.position, loadingLine.StopPoint.position) > station.maxPickupDistance)
            {
                // 임시 분기 정지점은 적재 지점이 아니다. 이송 경로가 이어질 때까지 적재/실패 처리를 하지 않는다.
                box.state = BoxState.Transferring;
                if (waitingTransfers.Add(box))
                    Debug.Log($"[Cell Transfer] {station.destination} | {box.boxId} 이송 대기 | 최종 적재 지점 미연결: {loadingLine.name}. 로봇 앞 정지점까지 NextLine 경로를 연결하세요.", this);
                yield return null;
                continue;
            }
            waitingTransfers.Remove(box);
            yield return LoadTerminalBox(station, loadingLine, box);
        }
    }

    void AdvanceTerminalTransfers(ConveyorLine start)
    {
        var visited = new HashSet<ConveyorLine>();
        for (var line = start; line != null && line.NextLine != null && visited.Add(line); line = line.NextLine)
        {
            if (!line.HasBoxAtStop || !line.NextLine.CanAccept) continue;
            line.CurrentBox.state = BoxState.Transferring;
            line.Release();
        }
    }

    static ConveyorLine TerminalEnd(ConveyorLine start)
    {
        var visited = new HashSet<ConveyorLine>();
        var line = start;
        while (line != null)
        {
            if (!visited.Add(line)) return null;
            if (line.NextLine == null) return line;
            line = line.NextLine;
        }
        return null;
    }

    IEnumerator LoadTerminalBox(TerminalStation station, ConveyorLine loadingLine, BoxInfo box)
    {
        var loader = station.loader;
        var collider = box.GetComponent<BoxCollider>();
        string error = null;
        if (box.IsMarked || box.destination == Destination.NG) error = "NG 상자가 일반 터미널에 도착했습니다.";
        else if (collider == null || collider.attachedRigidbody == null) error = "상자의 Collider/Rigidbody가 없습니다.";
        else if (Vector3.Distance(loader.transform.position, collider.bounds.center) > station.maxPickupDistance)
            error = $"정지점이 로봇 집기 범위 밖입니다 ({Vector3.Distance(loader.transform.position, collider.bounds.center):F2}m). 터미널 이송 경로를 연결하세요.";
        else if (loader.toto.Count >= loader.toto.Capacity)
        {
            // 수거함이 비워지면 자동으로 다시 확인한다. 만적을 적재 실패로 반복 집계하지 않는다.
            blockedLoads.Add(box);
            RaiseAlarm("수거함 가득 참 " + station.destination, box.boxId);
            while (IsRunning && !alarmActive && loader.toto.Count >= loader.toto.Capacity) yield return null;
            blockedLoads.Remove(box);
            yield break;
        }
        if (error != null) { BlockTerminal(station, box, error); yield break; }
        var body = collider.attachedRigidbody;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (alarmActive || !IsRunning || box == null) yield break;
            box.state = BoxState.Loading;
            if (!loader.TryLoadBox(collider, out error)) break;
            Debug.Log($"[Cell Loading] {station.destination} | {box.boxId} 적재 시작 | 시도 {attempt + 1}", this);
            float started = Time.time;
            while (loader.Busy && !alarmActive && IsRunning && Time.time - started < loadingTimeLimit) yield return null;
            bool timedOut = loader.Busy && Time.time - started >= loadingTimeLimit;
            if (loader.Busy) loader.StopLoading();
            // 배치는 성공했지만 팔 복귀가 실패한 경우도 실물 적재는 한 번만 집계한다.
            bool placed = false;
            foreach (var item in loader.PlacedBoxes) if (item == collider) { placed = true; break; }
            if (placed)
            {
                loadingLine.TakeAway(box);
                Finish(box, station.destination);
                if (!loader.LastLoadSucceeded) RaiseAlarm("적재 후 팔 복귀 실패 " + station.destination, box.boxId);
                yield break;
            }
            if (alarmActive || !IsRunning) yield break;
            error = timedOut ? "적재 제한 시간 초과" : loader.Status;
            // 흡착 중이거나 이미 이동한 상자를 자동으로 되돌리거나 다시 집지 않는다.
            if (loader.Attached || Vector3.Distance(collider.bounds.center, loadingLine.StopPoint.position + Vector3.up * collider.bounds.extents.y) > .05f) break;
            if (attempt == 0) { Debug.LogWarning($"[Cell Loading] {box.boxId} 적재 재시도 | {error}", this); yield return new WaitForSeconds(.5f); }
        }
        if (!loader.Attached && body != null) body.isKinematic = true;
        BlockTerminal(station, box, error ?? "적재 실패");
    }

    void BlockTerminal(TerminalStation station, BoxInfo box, string error)
    {
        box.state = BoxState.Sorting;
        blockedLoads.Add(box);
        Debug.LogWarning($"[Cell Loading] {station.destination} | {box.boxId} 정지 유지 | {error}", this);
        RaiseAlarm("적재 실패 " + station.destination, box.boxId);
    }

    public void RetryTerminalLoading(Destination terminal)
    {
        foreach (var station in terminalStations)
        {
            var end = TerminalEnd(station.line);
            if (station.destination == terminal && end != null && end.CurrentBox != null && !station.loader.Busy && !station.loader.Attached)
                blockedLoads.Remove(end.CurrentBox);
        }
    }

    void StopTerminalLoaders()
    {
        if (terminalStations == null) return;
        foreach (var station in terminalStations)
            if (station != null && station.loader != null && station.loader.Busy)
            {
                var end = TerminalEnd(station.line);
                if (end != null && end.CurrentBox != null) blockedLoads.Add(end.CurrentBox);
                station.loader.StopLoading();
            }
    }

    void OnDisable()
    {
        IsRunning = false;
        StopAllCoroutines();
        if (feeder != null) feeder.StopFeeding();
        StopTerminalLoaders();
    }

    // 교정 스테이션: 도착 대기 → JudgeAndCorrect() → 판독 구간으로 이송    (FR-03, FR-05, FR-06, FR-16)
    /// 운전이 끝날 때까지 상자를 하나씩 처리한다. 판독 구간 입구가 비어야 보낸다. (FR-16)
    IEnumerator CorrectStationLoop()
    {
        while (IsRunning)
        {
            //  1) 정지 위치에 상자가 올 때까지 기다린다
            if (!correctLine.HasBoxAtStop)
                { yield return null; continue; }

            BoxInfo box = correctLine.CurrentBox;
            box.correctArriveTime = Time.time;
            Mode = PendingMode;         // 제어 방식은 상자 단위로 바뀐다 (FR-12)
            box.mode = Mode;
            box.state = BoxState.Judging;
            Debug.Log($"[Cell] 교정 스테이션 도착 | {box.boxId} | 제어 방식 {Mode}", this);

            //  2) 측정 · 교정
            yield return JudgeAndCorrect(box);

            //  3) 판독 구간 입구가 빌 때까지 기다렸다가 보낸다. 그 사이 운전이 끝나면 상자는 그 자리에 둔다
            while (IsRunning && !readLine.CanAccept) { yield return null; }
            if (!IsRunning) { yield break; }

            box.state = BoxState.Transferring;
            correctLine.Release();
        }
    }

    // 측정 → 행동 선택(완료 또는 회전) → 회전 뒤 다시 측정. 완료 · 회전 한도 · 제한 시간 중 하나가 될 때까지 반복.
    /// [종료] 회전이 maxRotations에 닿거나 correctTimeLimit이 지나면 더 돌리지 않고 끝낸다 (예외 표시는 하지 않는다).
    ///        송장이 끝내 위로 오지 않은 상자는 판독 스테이션에서 NoLabel이 된다.
    /// [!] 카메라가 없어 측정하지 못하면 JudgeFail로 표시하고 바로 끝낸다. 교정 로봇이 실패해도 바로 끝낸다 (CorrectionFail 표시는 CorrectAt()이 한다).
    /// 측정 방식은 상자의 제어 방식(box.mode)을 따른다. 학습 정책이면 ONNX, 기본 제어면 색 비율.
    IEnumerator JudgeAndCorrect(BoxInfo box)
    {
        while (true)
        {
            //  1) 측정
            if (!Measure(box, box.mode == ControlMode.Policy)) { yield break; }

            //  2) 행동 선택. Top이면 완료
            BoxFace face = ChooseFace(box);
            if (face == BoxFace.Top)
            {
                string result = box.judgedFace == BoxFace.Top ? "송장 위" : "뒤집은 뒤에도 송장이 안 보임";
                Debug.Log($"[Cell] 교정 끝 | {box.boxId} | {result} | 회전 {box.rotationCount}회", this);
                yield break;
            }

            //  3) 회전 한도 · 제한 시간. 넘으면 지금 자세 그대로 보낸다
            bool limit = box.rotationCount >= Settings.maxRotations;
            if (limit || Time.time - box.correctArriveTime >= correctTimeLimit)
            {
                string why = limit ? $"회전 한도 {Settings.maxRotations}회" : $"제한 시간 {correctTimeLimit:0}초";
                Debug.LogWarning($"[Cell] 교정 중단 ({why}) | {box.boxId} | 판정 {box.judgedFace} 그대로 보냄", this);
                yield break;
            }

            //  4) 올릴 면을 위로 돌린다. 교정 로봇이 없으면 그 자리에서 바로 돌린다 (자리 표시)
            /// 상자 물리를 켜기는 CorrectAt(), 끄기는 ConveyorLine.Release()가 한다 (벨트 위 상자는 kinematic).
            box.state = BoxState.Correcting;
            box.chosenRotation = Quaternion.FromToRotation(BoxInfo.FaceDirection(face), Vector3.up).eulerAngles;
            if (correction != null) { yield return correction.CorrectAt(box, face); }
            else { yield return RotateInPlace(box, face); }

            box.rotationCount++;
            if (face == BoxFace.Bottom) { box.flipped = true; }

            //  5) 로봇 교정이 실패하면 경고를 올리고 끝낸다. 상자는 CorrectAt()이 CorrectionFail로 표시했고, 판독 구간을 거쳐 NG로 간다
            if (correction != null && !correction.LastSuccess)
            {
                Debug.LogWarning($"[Cell] 교정 실패 | {box.boxId} | {correction.FailReason}", this);
                RaiseAlarm("교정 실패", box.boxId);
                yield break;
            }

            //  끝나면 1)로 돌아가 다시 측정
            box.state = BoxState.Judging;
        }
    }

    // (지훈님) 바깥에서 송장 면 판정만 할 때 부른다 (FaceJudgeAgent, InferenceIntegrationCheck).
    /// 제어 방식과 관계없이 Face Detector가 연결되어 있으면 ONNX로, 없으면 색 비율로 판정한다.
    public bool TryJudgeFace(BoxInfo box) => box != null && Measure(box, faceDetector != null);

    //  카메라 5대로 송장 면을 판정해 면별 점수를 box.faceRatios에, 판정한 면을 box.judgedFace에 기록한다.
    /// useModel = true: 송장 유무 ONNX 점수 (LabelPresenceDetector.TryJudgeBox()). false: 송장 색 비율 (FaceCameraRig.Measure()).
    /// 두 방식 모두 기준 이상 중 가장 높은 면, 모두 기준 미만이면 Bottom.
    /// 상자만 잠깐 카메라 레이어로 옮겨 찍고 되돌린다 (한 프레임 안에 끝남).
    /// [주의] 카메라나 모델이 없거나 판정에 실패하면 JudgeFail로 표시하고 false.
    bool Measure(BoxInfo box, bool useModel)
    {
        if (faceRig == null || !faceRig.IsBuilt)
        {
            box.Mark(ExceptionReason.JudgeFail);
            RaiseAlarm("측정 실패", box.boxId);
            return false;
        }

        //  학습 정책: 송장 유무 ONNX로 판정한다 (상자 레이어 옮기기와 점수 기록은 TryJudgeBox()가 한다)
        if (useModel)
        {
            if (faceDetector == null)
            {
                box.Mark(ExceptionReason.JudgeFail);
                RaiseAlarm("ONNX 판정 실패: Face Detector 칸이 비어 있습니다", box.boxId);
                return false;
            }
            if (!faceDetector.TryJudgeBox(box, faceRig, out string error))
            {
                box.Mark(ExceptionReason.JudgeFail);
                RaiseAlarm("ONNX 판정 실패: " + error, box.boxId);
                return false;
            }
            Debug.Log($"[Cell ONNX] {box.boxId} → {box.judgedFace} | {faceDetector.LastMilliseconds:F2} ms", this);
            return true;
        }

        //  기본 제어: 송장 색 비율로 판정한다
        //  1) 상자만 카메라 레이어로 옮겨 5면을 찍고, 원래 레이어로 되돌린다
        int boxLayer = box.gameObject.layer;
        int rigLayer = RigLayer();
        if (rigLayer >= 0) { SetLayer(box.gameObject, rigLayer); }
        float[] ratios = faceRig.Measure();
        if (rigLayer >= 0) { SetLayer(box.gameObject, boxLayer); }

        //  2) 카메라 순서(CapturedFaces) --> faceRatios 순서(BoxFace). 지금은 두 순서가 같다
        for (int i = 0; i < FaceCameraRig.FaceCount; i++)
            { box.faceRatios[(int)FaceCameraRig.CapturedFaces[i]] = ratios[i]; }
        box.judgedFace = faceRig.JudgeFace(ratios);

        //  3) 로그. 송장이 있는 상자는 실제 송장 방향(LabelFaceNow)과 비교해, 판정이 다르면 경고한다 (테스트 확인용)
        string text = $"{box.boxId} 측정 | 판정 {box.judgedFace} | " +
                      $"위 {box.faceRatios[0]:0.000} · 앞 {box.faceRatios[1]:0.000} · 뒤 {box.faceRatios[2]:0.000} · " +
                      $"왼 {box.faceRatios[3]:0.000} · 오 {box.faceRatios[4]:0.000}";
        BoxFace actual = box.LabelFaceNow;
        if (actual != BoxFace.None && actual != box.judgedFace)
            { Debug.LogWarning($"[Cell] {text} | 실제 {actual} (판정 불일치)", this); }
        else { Debug.Log($"[Cell] {text}", this); }
        return true;
    }

    //  행동 선택: 위로 올릴 면을 돌려준다. Top이면 완료.
    /// 판정한 면(judgedFace)을 그대로 쓴다 (두 제어 방식 같음). 이미 뒤집었는데 또 Bottom이면 송장이 없는 상자로 보고 완료로 둔다.
    BoxFace ChooseFace(BoxInfo box)
    {
        if (box.judgedFace == BoxFace.Bottom && box.flipped) { return BoxFace.Top; }
        return box.judgedFace;
    }

    //  [자리 표시] 올릴 면이 위로 오게 상자를 그 자리에서 바로 돌리고, 로봇 동작 시간만큼 기다린다.
    /// 바닥 높이는 그대로 두고 중심 높이만 다시 맞춘다 (5*6*7cm 상자는 눕히면 높이가 바뀐다).
    /// correction 칸이 비었을 때만 쓴다 (로봇 없는 테스트 씬).
    IEnumerator RotateInPlace(BoxInfo box, BoxFace face)
    {
        Transform boxTransform = box.transform;
        float bottom = boxTransform.position.y - ConveyorLine.HalfSize(box, Vector3.up);

        boxTransform.rotation = Quaternion.FromToRotation(BoxInfo.FaceDirection(face), Vector3.up) * boxTransform.rotation;
        Vector3 position = boxTransform.position;
        position.y = bottom + ConveyorLine.HalfSize(box, Vector3.up);
        boxTransform.position = position;

        //  kinematic 상자는 물리 엔진 쪽 위치 · 회전도 같이 맞춘다 (ConveyorLine은 Rigidbody 위치로 잰다)
        Rigidbody body = box.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = position;
            body.rotation = boxTransform.rotation;
        }

        Debug.Log($"[Cell] 회전 (자리 표시) | {box.boxId} | {face} 면을 위로", this);
        yield return new WaitForSeconds(face == BoxFace.Bottom ? 2f : 1f);          // 바닥면은 90° 두 번
    }

    //  카메라 리그가 보는 레이어 (Culling Mask에서 켜진 첫 레이어).
    /// 모든 레이어를 보거나 켜진 레이어가 없으면 -1 (상자 레이어를 바꾸지 않는다).
    int RigLayer()
    {
        if (faceRig.SeesEverything) { return -1; }

        int mask = faceRig.CullingMask.value;
        for (int layer = 0; layer < 32; layer++)
            if ((mask & (1 << layer)) != 0) { return layer; }
        return -1;
    }

    //  상자와 그 아래 오브젝트(꺼진 것 포함)의 레이어를 모두 바꾼다.
    static void SetLayer(GameObject root, int layer)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            { child.gameObject.layer = layer; }
    }

    // 판독 스테이션: 도착 대기 → 판독 → 검증 → box.destination 기록 → 분류 구간으로 이송    (FR-04, FR-07)
    /// 운전이 끝날 때까지 상자를 하나씩 처리한다.
    /// 판독 · 검증은 LabelReader(지훈님, 송장 OCR ONNX)가 하고, 제어 방식과 관계없다.
    IEnumerator ReadStationLoop()
    {
        while (IsRunning)
        {
            //  1) 정지 위치에 상자가 올 때까지 기다린다
            if (!readLine.HasBoxAtStop)
                { yield return null; continue; }

            BoxInfo box = readLine.CurrentBox;
            box.readArriveTime = Time.time;
            box.state = BoxState.Reading;
            Debug.Log($"[Cell] 판독 스테이션 도착 | {box.boxId} | 교정 -> 판독 {box.readArriveTime - box.correctArriveTime:F1}초", this);

            //  2) 판독 · 검증. LabelReader가 OCR · 규칙 검증을 하고 목적지 또는 예외를 기록한다 (reader.Read()).
            //     연결이 없으면 성공으로 처리하지 않고 ReadFail로 보낸다.
            //     테스트 씬에서 placeholderRead를 켜면 자리 표시: 1초 기다린 뒤 송장 정답으로 목적지를 정한다 (PlaceholderRead()).
            if (reader != null) { yield return reader.Read(box); }
            else if (placeholderRead) { yield return new WaitForSeconds(1f); }
            else { box.Mark(ExceptionReason.ReadFail); Debug.LogError("[Cell] LabelReader가 연결되지 않았습니다.", this); }
            if (!IsRunning) { yield break; }
            if (reader == null && placeholderRead) { PlaceholderRead(box); }

            //  3) 분류 구간으로 보낸다
            box.state = BoxState.Sorting;
            readLine.Release();
        }
    }

    //  [자리 표시] 판독 결과 대신 송장 정답으로 목적지를 정한다. 분류 구간(푸셔)을 시험하기 위한 것.
    /// 이미 예외가 표시된 상자는 그대로 둔다. 송장이 윗면에 없으면 NoLabel, 자리수 오류는 ReadFail, 그 밖의 규칙 위반은 RuleViolation.
    /// [호출] LabelReader가 비어 있고 placeholderRead가 켜져 있을 때만 (테스트 씬).
    void PlaceholderRead(BoxInfo box)
    {
        if (!box.IsMarked)
        {
            Destination answer = ExpectedDestination(box);
            if (box.LabelFaceNow != BoxFace.Top) { box.Mark(ExceptionReason.NoLabel); }         //  송장 없는 상자도 여기(LabelFaceNow = None)
            else if (answer != Destination.NG) { box.destination = answer; }
            else if ((box.labelNumber ?? "").Length != 5) { box.Mark(ExceptionReason.ReadFail); }
            else { box.Mark(ExceptionReason.RuleViolation); }
        }
        Debug.Log($"[Cell] 판독 (자리 표시) | {box.boxId} | 목적지 {box.destination}{(box.IsMarked ? $" ({box.reason})" : "")}", this);
    }


    //  ============================================================
    //  점검 · 요약
    //  ============================================================
                        ///// : TODO : 
    // 필수 칸 확인. 빠진 칸이 있으면 이름을 알려 주고 false.
    /// [다음 단계] 연결 칸을 열 때마다 여기에 추가한다.
    bool Check()
    {
        string missing = settings == null ? "Settings"
                       : correctLine == null ? "Correct Line"
                       : readLine == null ? "Read Line"
                       : feeder == null ? "Feeder"
                       : faceRig == null ? "Face Rig"
                       : null;
        if (missing != null)
        {
            Debug.LogWarning($"[Cell] {missing} 칸이 비어 있어 운전을 시작하지 않습니다.", this);
            return false;
        }
        if (!ValidateTerminalConfiguration(out string terminalError))
        { Debug.LogWarning("[Cell] " + terminalError, this); return false; }

        //  카메라 리그: 카메라 5대가 없으면 시작하지 않는다. 모든 레이어를 보면 경고만 한다
        if (!faceRig.IsBuilt)
        {
            Debug.LogWarning("[Cell] Face Rig에 카메라가 없습니다. FaceCameraRig의 '카메라 5대 배치 / 갱신' 버튼을 누르세요.", faceRig);
            return false;
        }
        //  학습 정책: 모델이 없거나 모델 규약이 맞지 않으면 시작하지 않는다 (기본 제어는 모델을 보지 않는다)
        if (PendingMode == ControlMode.Policy)
        {
            string modelError = "Face Detector 칸에 모델이 없습니다.";
            if (!HasPolicy || !faceDetector.ValidateConfiguration(out modelError))
            {
                Debug.LogWarning("[Cell] ONNX 준비 실패: " + modelError, this);
                return false;
            }
        }
        if (faceRig.SeesEverything)
        {
            Debug.LogWarning(
                "[Cell] Face Rig가 모든 레이어를 봅니다. 벨트 · 로봇까지 찍혀 판정이 틀립니다. '상자 레이어(ScanTarget)만 보기' 버튼을 누르세요.",
                faceRig);
        }

        //  교정 로봇: 비어 있으면 자리 표시로 돌린다. 있으면 제한 시간이 로봇 동작보다 짧은지 본다
        if (correction == null) { Debug.Log("[Cell] Correction 칸이 비어 있어 상자를 그 자리에서 돌립니다 (자리 표시).", this); }
        else if (correctTimeLimit < 60f)
        {
            Debug.LogWarning(
                $"[Cell] Correct Time Limit {correctTimeLimit:0}초는 교정 로봇 1회보다 짧을 수 있습니다. 첫 회전 뒤 시간이 지나 있으면 두 번째 회전 없이 보냅니다.",
                this);
        }
        return true;
    }

    // Console에 남기는 집계 요약
    string Summary()
    {
        string reasons = "";
        for (int i = 1; i < ExceptionsByReason.Length; i++)
            if (ExceptionsByReason[i] > 0) { reasons += $" {(ExceptionReason)i} {ExceptionsByReason[i]}"; }

        return $"처리 {Processed} | 성공 {Success} · 실패 {Fail} · 예외 {Exceptions}{(reasons == "" ? "" : $" ({reasons.Trim()})")} | " +
               $"A {TerminalCounts[0]} · B {TerminalCounts[1]} · C {TerminalCounts[2]} | 회전 합 {Corrections} | " +
               $"예외율 {ExceptionRate * 100f:0.#}% | 시간당 {ThroughputPerHour:0}개";
    }


    //  ============================================================
    //  테스트 (CellController ⋮ 메뉴, Play 중에만)
    //  ============================================================
    // Play 중인지 확인 (아니면 경고)
    bool Playing()
    {
        if (Application.isPlaying) { return true; }
        Debug.LogWarning("[Cell] Play 중에만 실행할 수 있습니다.", this);
        return false;
    }

    [ContextMenu("테스트/운전 1개 - Run(1)")]
    void TestRunOne()
    {
        if (Playing()) { Run(1); }
    }

    [ContextMenu("테스트/연속 운전 - Run(0)")]
    void TestRunContinuous()
    {
        if (Playing()) { Run(0); }
    }

    [ContextMenu("테스트/사이클 정지")]
    void TestCycleStop()
    {
        if (Playing()) { RequestCycleStop(); }
    }

    // [테스트] 가상 상자를 만들어 Finish()로 넘긴다 (상자 Prefab · 컨베이어 없이 집계 확인).
    [ContextMenu("테스트/가상 상자 1개 처리 (Finish)")]
    void TestFinishOne()
    {
        if (!Playing() || Settings == null) { return; }
        FinishFakeBox();
        Debug.Log($"[Cell] {Summary()}", this);
    }

    [ContextMenu("테스트/가상 상자 10개 처리 (Finish)")]
    void TestFinishTen()
    {
        if (!Playing() || Settings == null) { return; }
        for (int i = 0; i < 10; i++) { FinishFakeBox(); }
        Debug.Log($"[Cell] {Summary()}", this);
    }

    // 가상 상자 1개: 송장 정답을 무작위로 정하고, 80% 맞는 터미널 · 10% 다른 터미널(분류 실패) · 10% 무작위 예외로 보낸다.
    void FinishFakeBox()
    {
        BoxInfo box = new GameObject().AddComponent<BoxInfo>();
        testCount++;
        box.boxId = $"TEST-{testCount:0000}";
        box.name = box.boxId;

        int index = Random.Range(0, Settings.regionCodes.Length);
        box.labelNumber = $"{Settings.regionCodes[index]:00}{Random.Range(0, 1000):000}";
        box.labelRegion = Settings.regionNames[index];
        box.rotationCount = Random.Range(0, Settings.maxRotations + 1);

        float roll = Random.value;
        if (roll < 0.8f) { box.destination = Settings.terminals[index]; }
        else if (roll < 0.9f) { box.destination = Settings.terminals[(index + 1) % Settings.terminals.Length]; }
        else { box.Mark((ExceptionReason)Random.Range(1, ExceptionsByReason.Length)); }

        Finish(box);
        Destroy(box.gameObject);
    }

    [ContextMenu("테스트/같은 경고 연속 (알람 정지)")]
    void TestAlarm()
    {
        if (!Playing()) { return; }
        for (int i = 0; i < maxConsecutiveErrors; i++) { RaiseAlarm("테스트 경고", $"TEST-{i + 1:0000}"); }
    }

    [ContextMenu("테스트/알람 해제")]
    void TestResetAlarm()
    {
        if (Playing()) { ResetAlarm(); }
    }

    [ContextMenu("테스트/학습 정책으로 바꾸기")]
    void TestSetPolicy()
    {
        if (Playing()) { SetMode(ControlMode.Policy); }
    }

    [ContextMenu("테스트/기본 제어로 바꾸기")]
    void TestSetBasic()
    {
        if (Playing()) { SetMode(ControlMode.Basic); }
    }
}
