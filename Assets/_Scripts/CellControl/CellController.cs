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
//  ============================================================
public class CellController : MonoBehaviour
{
    //  ---------------- 설정 ----------------    (NFR-09)
    [Header("설정")]
    [Tooltip("공정 파라미터 에셋 (CellSettings). 시작할 때 복사본을 만들어 Settings로 나눠 준다")]
    [SerializeField] CellSettings settings;

    [Tooltip("같은 경고가 이 횟수만큼 연속되면 알람으로 멈춘다 (default: 3)")]
    [Min(1)][SerializeField] int maxConsecutiveErrors = 3;

    //  ---------------- 연결 ----------------
    [Header("연결")]
    [Tooltip("교정 스테이션 구간 (정지 위치가 있는 ConveyorLine)")]
    [SerializeField] ConveyorLine correctLine;

    [Tooltip("판독 스테이션 구간 (정지 위치가 있는 ConveyorLine)")]
    [SerializeField] ConveyorLine readLine;

    [Tooltip("상자 투입 (BoxFeeder)")]
    [SerializeField] BoxFeeder feeder;

                    ///// : TODO : 
    //  [스크립트 구현 마다] 아래 칸은 해당 스크립트를 만들 때 하나씩 연다.
    //      faceRig (FaceCameraRig)                  카메라 5대 송장 위치 측정
    //      correction (CorrectionSequence)          교정 로봇암 동작 (손가락 그리퍼, 물리 파지)
    //      loaders (LoadSequence[3])                터미널 끝 흡착 로봇 (Check()와 알람 정지용)
    //      correctionAgent (CorrectionAgent)        ML ① 교정 학습 정책
    //      reader (LabelReaderAgent)                ML ② 송장 판독 정책
    //      log (ProductionLog)                      세션 로그
    //      제한 시간: supplyTimeout 30, correctTimeLimit 10, readTimeout 2, inferenceTimeout 1 (초)


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

                    ///// : TODO : 
    /// [CorrectionAgent 구현 후] 그 모델 칸을 확인하도록 바꾼다. 지금은 항상 false.
    /// CorrectionAgent에 학습된 모델(.onnx)이 연결되어 있는지.
    public bool HasPolicy => false;

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
                                ///// : TODO : 
            //  [다음 단계] 벨트 · 교정 로봇 · 끝 로봇 3대 정지
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
    {
        if (box == null || box.IsFinished) { return; }

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
            int terminal = (int)box.destination - 1;    //  A 0, B 1, C 2
            if (terminal >= 0 && terminal < TerminalCounts.Length) { TerminalCounts[terminal]++; }

            Destination answer = ExpectedDestination(box);
            if (box.destination == answer)
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

        Debug.Log($"[Cell] {box.boxId} {result} | 목적지 {box.destination} | 회전 {box.rotationCount}회", this);
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
        if(!feeder.IsFeeding)
        {
            State = CellState.Stopped;
            IsRunning = false;
            Debug.LogWarning("[Cell] 투입을 시작하지 못해 운전을 멈춥니다. Feeder 경고를 확인하세요.", this);
            yield break;
        }

        Coroutine correctLoop = StartCoroutine(CorrectStationLoop());
        Coroutine readLoop = StartCoroutine(ReadStationLoop());

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

    // 교정 스테이션: 도착 대기 → JudgeAndCorrect() → 판독 구간으로 이송    (FR-03, FR-05, FR-06, FR-16)
    /// 운전이 끝날 때까지 상자를 하나씩 처리한다. 판독 구간 입구가 비어야 보낸다. (FR-16)
    IEnumerator CorrectStationLoop()
    {
        while(IsRunning)
        {
            //  1) 정지 위치에 상자가 올 때까지 기다린다
            if(!correctLine.HasBoxAtStop)
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

    // 측정 → 행동 선택(완료 또는 회전) → 회전 뒤 다시 측정. 완료가 나오거나 제한 시간이 될 때까지 반복.
    /// [Correction ML 준비 완료 후] FaceCameraRig, CorrectionSequence, CorrectionAgent 연결 후 채운다.
    IEnumerator JudgeAndCorrect(BoxInfo box)
    {
                            ///// : TODO :
        //  [Correction ML 준비 완료 후] 아래 자리 표시(작업 시간 1초 흉내)를 지우고 실제 동작으로 바꾼다 
        /// 상자 물리를 켜고 끄는 일은 CorrectionSequence가 한다 (벨트 위 상자는 kinematic).
        yield return new WaitForSeconds(1f);
    }

    // 판독 스테이션: 도착 대기 → 판독 → 검증 → box.destination 기록 → 분류 구간으로 이송    (FR-04, FR-07)
    /// 운전이 끝날 때까지 상자를 하나씩 처리한다.
    /// [Label ML 준비 완료 후] LabelReaderAgent, LabelValidator 연결 후 채운다.
    IEnumerator ReadStationLoop()
    {
        while(IsRunning)
        {
            //  1) 정지 위치에 상자가 올 때까지 기다린다
            if(!readLine.HasBoxAtStop)
                { yield return null; continue; }

            BoxInfo box = readLine.CurrentBox;
            box.readArriveTime = Time.time;
            box.state = BoxState.Reading;
            Debug.Log($"[Cell] 판독 스테이션 도착 | {box.boxId} | 교정 -> 판독 {box.readArriveTime - box.correctArriveTime:F1}초", this);

            //  2) 판독 · 검증
            ///// : TODO :
            //  [Label ML 준비 완료 후] reader.Read(box) -> LabelValidator.Validate -> box.destination 기록.
            //  지금은 자리 표시(작업 시간 1초 흉내)만 둔다.
            yield return new WaitForSeconds(1f);
            if (!IsRunning) { yield break; }

            //  3) 분류 구간으로 보낸다
            box.state = BoxState.Sorting;
            readLine.Release();
        }
    }


    //  ============================================================
    //  점검 · 요약
    //  ============================================================
                    ///// : TODO : 
    /// [다음 단계] 연결 칸을 열 때마다 여기에 추가한다.
    // 필수 칸 확인. 빠진 칸이 있으면 이름을 알려 주고 false.
    bool Check()
    {
        string missing = settings == null ? "Settings"
                       : correctLine == null ? "Correct Line"
                       : readLine == null ? "Read Line"
                       : feeder == null ? "Feeder"
                       : null;
        if (missing == null) { return true; }

        Debug.LogWarning($"[Cell] {missing} 칸이 비어 있어 운전을 시작하지 않습니다.", this);
        return false;
    }

    // Console에 남기는 집계 요약
    string Summary()
    {
        string reasons = "";
        for (int i = 1; i < ExceptionsByReason.Length; i++)
            if (ExceptionsByReason[i] > 0) 
                { reasons += $" {(ExceptionReason)i} {ExceptionsByReason[i]}"; }
        
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
}
