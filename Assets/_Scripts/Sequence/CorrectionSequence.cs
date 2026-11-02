using System.Collections;
using System.Linq;
using UnityEngine;

//  ============================================================
//  CorrectionSequence
//  교정 스테이션의 로봇암(FR5)이 상자 옆면을 손가락 그리퍼로 집어 들고, 손목으로 90° 돌린 뒤 같은 벨트 위에 내려놓는다.
//      - 위로 올릴 면(faceToRaise)이 옆면이면 90° 1번, 바닥면이면 90° 2번(중간에 내려놓고 다시 집기)으로 올린다.
//      - 이동은 ArmIK, 집기는 GripperController(손가락 마찰)가 한다. 이 스크립트는 순서와 성공 · 실패 판단을 맡는다.
//      - 공정에서는 CellController가 CorrectAt(상자, 면)을 부르고, 끝나면 LastSuccess를 본다.
//
//  [순서]
//      1. 열기
//      → 2. 옆면 접근
//      → 3. 잡기 위치
//      → 4. 손가락 높이 확인 
//      → 5. 닫기
//      → 6. 들어 올리기
//      → 7. 90° 회전
//      → 8. 내려놓기
//      → 9. 놓기
//      → 10. 물러나기
//      → (11. 준비 자세 → (바닥면이면) 1.부터 한 번 더)
//  
//  [사용] 로봇 루트(fr5_with_gripper)에 GripperController와 함께 붙인다. ArmIK는 자동으로 같이 붙는다.
//         Tcp(손가락 사이 기준점), Gripper, Placement Surface(교정 구간 벨트 Collider)를 연결하고,
//         Tools/Bonguri/Set Raised Ready Pose로 준비 자세 관절각을 채운다.
//  [!] 벨트 위 상자는 kinematic이다. CorrectAt()이 집기 전에 물리를 켜고, 내려놓은 뒤 상자가 멈출 때까지 기다린다.
//      다시 kinematic으로 되돌리고 놓인 자리를 점검하는 일은 ConveyorLine.Release()가 한다.
//  [!] 교정 로봇은 ConveyorLine.TakeAway()를 부르지 않는다. 상자는 같은 정지 위치로 돌아온다.
//
//  [설계서와의 차이]
//      * 도착 확인에 ArmIK.Arrived를 쓴다 (ArmIK에 추가). Moving이 false가 되어도 정지 · 경로 중단 · 시간 초과일 수 있어서.
//      * 옆면 접근과 90° 돌리기 경로는 ArmIK.MoveThrough()(여러 점을 곧게 잇는 경로, ArmIK에 추가)로 만든다.
//        높이 · 손목 방향 · 거리를 바꿔 가며 처음 계획되는 경로를 쓴다 (설계서에 경로 정하는 법은 없음).
//      * 바닥면은 로봇 쪽 옆면을 먼저 세운다. 그러면 바닥면이 로봇 쪽을 보게 되어 두 번 모두 로봇 쪽에서 집는다.
//      * 손가락 닫힘량은 집는 방향의 상자 폭에 맞춰 자동으로 정한다 (parcelGripCloseAmount는 폭 6 cm 기준).
//        설계서: 상자 크기나 집는 면이 바뀌면 이 값을 다시 맞춘다.
//      * 내려놓은 뒤 "놓기 · 물러나기"를 PlaceTurnedParcel()과 나눴다 (버튼으로 시험: 내려놓기 → 그리퍼 열기 → 들어 올리기).
//      * 실패하면 상자를 벨트 위로 내리고 손을 연 뒤 준비 자세로 돌아간다 (가이드 AssemblySequence: 올라가기 → 열기 → GoHome).
//      * travelClearance, boxClearance, compensateRobotGravity, closeTime, liftCheck 칸과 FailReason을 더했다.
//        motionTimeout은 설계 메모에만 있어서 칸으로 만들었다.
//      * 올릴 면은 CellTypes의 BoxFace를 쓴다 (설계서: 중첩 enum ParcelFace, 앞 · 뒤가 BoxFace와 반대). 그래서 FaceToRaise() 변환은 없앴다.
//  ============================================================
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(ArmIK))]
public class CorrectionSequence : MonoBehaviour
{
    //  ---------------- 올릴 면 ----------------    (FR-05)
    [Header("올릴 면")]
    [Tooltip("위로 올릴 면 (처음 놓인 상태의 월드 축 기준: 앞 −Z, 뒤 +Z, 왼쪽 −X, 오른쪽 +X, 바닥 −Y). 위 · 없음이면 돌리지 않는다. 공정에서는 CorrectAt()이 상자마다 정한다")]
    public BoxFace faceToRaise = BoxFace.Front;

    //  ---------------- 연결 ----------------
    [Header("연결")]
    [Tooltip("손가락 사이 기준점 (로봇 아래 Tool Center Point). ArmIK.tcp로 넘긴다")]
    public Transform tcp;

    [Tooltip("집을 상자의 BoxCollider. 공정에서는 CorrectAt()이 상자마다 바꾼다. Inspector 버튼 · 에디터 도구는 이 칸을 쓴다")]
    public BoxCollider targetBox;

    [Tooltip("손가락 그리퍼 (같은 로봇의 GripperController)")]
    public GripperController gripper;

    [Tooltip("내려놓을 벨트 면 (교정 구간 벨트의 Collider). 비우면 이름에 Conveyor가 들어간 오브젝트를 찾는다")]
    public Collider placementSurface;

                        /////   : TODO :
    /// ArmIK -> FrontArmIK & BackArmIK 클래스 분리된 점 반영
    [Tooltip("같은 오브젝트의 ArmIK. 비우면 찾는다")]
    [SerializeField] ArmIK ik;

    //  ---------------- 이동 ----------------
    [Header("이동")]
    [Tooltip("옆 대기 위치에서 상자 옆면까지 거리의 상한 m. 0.15 · 0.10 · 0.07 m 순서로 경로를 찾고, 이 값보다 멀리는 가지 않는다 (default: 0.12)")]
    [Min(0.02f)] public float approachHeight = 0.12f;

    [Tooltip("들어 올리는 높이 m. 놓은 뒤 위로 빠질 때도 쓴다 (default: 0.10)")]
    [Min(0.01f)] public float liftHeight = 0.10f;

    [Tooltip("관절 속도 도/초 (default: 20)")]
    [Range(1f, 60f)] public float degreesPerSecond = 20f;

    [Tooltip("손끝 직선 이동 속도 m/s (default: 0.06)")]
    [Min(0.005f)] public float metresPerSecond = 0.06f;

    [Tooltip("도착 허용 위치 오차 m (default: 0.008)")]
    public float positionTolerance = 0.008f;

    [Tooltip("도착 허용 각도 오차 ° (default: 4)")]
    public float angleTolerance = 4f;

    [Tooltip("옆 대기 위치로 갈 때 먼저 올라가는 높이 m. 이 값의 0 · 1 · 0.65 · 1.4배를 차례로 시도한다 (default: 0.20)")]
    [Min(0.05f)] public float travelClearance = 0.20f;

    [Tooltip("접근 · 복귀할 때 팔과 상자 사이에 둘 최소 간격 m (default: 0.008)")]
    [Min(0.001f)] public float boxClearance = 0.008f;

    [Tooltip("로봇 링크의 무게를 상쇄한다. 상자에는 중력이 그대로 걸린다 (default: 켬)")]
    public bool compensateRobotGravity = true;

    [Tooltip("켜면 옆면을 집고 상자 위에서 손목으로 돌린다. 끄면 위에서 집는다 (집기 시험용) (default: 켬)")]
    public bool sideGrasp = true;

    //  ---------------- 잡기 ----------------
    [Header("잡기")]
    [Tooltip("손가락 닫힘 목표 m (손가락 하나 기준, 0 열림 ~ 0.04 닫힘). 폭 6 cm 상자 기준이고, 집는 방향의 상자 폭에 맞춰 자동으로 보정한다 (default: 0.025)")]
    [Range(0f, 0.04f)] public float parcelGripCloseAmount = 0.025f;

    [Tooltip("손가락을 닫는 시간(초). 이 시간 동안 천천히 닫는다 (default: 3)")]
    [Min(0.1f)][SerializeField] float closeTime = 3f;

    [Tooltip("들어 올린 뒤 상자 바닥이 이만큼 올라와야 잡은 것으로 본다 m (default: 0.03)")]
    [Min(0.005f)][SerializeField] float liftCheck = 0.03f;

    //  ---------------- 시간 · 준비 자세 ----------------
    [Header("시간 · 준비 자세")]
    [Tooltip("동작 1번(접근, 들기, 돌리기 등)의 제한 시간(초). 넘으면 교정 실패 (default: 30)")]
    [Min(1f)][SerializeField] float motionTimeout = 30f;

    [Tooltip("준비 자세 관절각 6개 (도): shoulder, upperarm, forearm, wrist1, wrist2, wrist3. Tools/Bonguri/Set Raised Ready Pose가 채운다")]
    [SerializeField] float[] initialJointAngles;

    //  ---------------- 테스트 ----------------
    [Header("테스트")]
    [Tooltip("(선택) 테스트 상자를 가져올 구간. 그 구간 정지 위치의 상자를 교정한다 (비우면 Target Box)")]
    [SerializeField] ConveyorLine testLine;

    [Tooltip("켜면 화면에 테스트 패널을 그린다")]
    [SerializeField] bool showTestPanel = false;

    [Tooltip("테스트 패널 위치 (x, y). 다른 패널과 겹치지 않게 바꾼다")]
    [SerializeField] Vector2 guiPosition = new Vector2(760, 10);


    //  ============================================================
    //  상태 (CellController · 에디터 도구 · 테스트 패널이 읽는 값)
    //  ============================================================
    /// 상태 문구 (ArmIK 문구 포함), 지금 단계 이름
    public string Status => status;
    public string CycleStep { get; private set; } = "대기";

    /// 팔이 움직이는 중, 면 올리기 순서가 도는 중
    public bool Moving => Arm != null && Arm.Moving;
    public bool SequenceRunning { get; private set; }

    /// 지금 단계(1 ~ 2), 끝낸 90° 회전 수
    public int CurrentRotationStage { get; private set; }
    public int CompletedQuarterTurns { get; private set; }

    /// 마지막 교정 성공 여부, 실패 사유 (처음 생긴 사유만 남긴다)
    public bool LastSuccess { get; private set; }
    public string FailReason { get; private set; }

    /// 이동 목표와 오차 (ArmIK 값. 에디터 도구가 읽는다)
    public Vector3 GoalPosition => Arm != null ? Arm.GoalPosition : Vector3.zero;
    public Quaternion GoalRotation => Arm != null ? Arm.GoalRotation : Quaternion.identity;
    public float PositionError => Arm != null ? Arm.PositionError : 0f;

    ArmIK Arm
    {
        get
        {
            if (ik == null) { ik = GetComponent<ArmIK>(); }
            return ik;
        }
    }

    string status = "Play 후 Inspector 버튼을 누르거나 CellController가 교정을 시작한다.";
    bool failed;                                            //  이번 순서에서 실패가 생겼는지
    bool stepStarted;                                       //  마지막 단계 동작이 이동을 시작했는지 (WaitArm()이 본다)
    bool armArrived;                                        //  WaitArm()의 결과
    Vector3 raiseLocal;                                     //  올릴 면 (상자 로컬 축). 돌리는 동안 월드 방향이 바뀌어 로컬로 기억한다
    Vector3 stageLocal;                                     //  이번 90°에 세울 면 (상자 로컬 축)
    bool hasStage;                                          //  stageLocal이 정해졌는지 (아니면 로봇 쪽 옆면을 쓴다)
    float closingSign = 1f;                                 //  손목 방향 (+1 / −1). ApproachBox()가 고른다
    float standOff = 0.15f;                                 //  옆 대기 위치 거리 m. ApproachBox()가 고른다
    Vector3 placementAnchor;                                //  처음 상자 중심. 내려놓을 때 이 자리 위로 맞춘다


    //  ============================================================
    //  시작 · 끝
    //  ============================================================
    // ArmIK에 값을 넘기고 준비 자세로 시작한다.
    /// ArmIK보다 먼저 준비되도록 Awake()에서 한다 (두 스크립트 모두 실행 순서 -1000).
    void Awake()
    {
        ConfigureArm();
        Arm.Initialize(initialJointAngles);
    }

    void OnEnable()
    {
        if (Arm != null) { Arm.StatusChanged += OnArmStatus; }
    }

    // 첫 물리 스텝 뒤 빠진 칸을 확인한다. 빠진 칸이 있으면 경고만 남긴다.
    /// 관절은 ArmIK.Initialize()가 준비 자세를 목표로 붙잡는다. 여기서 Hold()로 지금 관절각을 다시 잡으면 준비 자세로 가다가 멈춘다.
    IEnumerator Start()
    {
        yield return new WaitForFixedUpdate();

        if (tcp == null || gripper == null)
        {
            Debug.LogWarning($"[Correct] {name}: Tcp 또는 Gripper 칸이 비어 있습니다. 교정을 하지 않습니다.", this);
            yield break;
        }
        if (initialJointAngles == null || initialJointAngles.Length != 6)
        { Debug.LogWarning($"[Correct] {name}: 준비 자세 관절각이 없습니다. Tools/Bonguri/Set Raised Ready Pose로 채우세요.", this); }

        SetStatus("준비 완료");
    }

    // 꺼질 때 순서를 끊는다.
    void OnDisable()
    {
        if (SequenceRunning) { Fail("교정 로봇이 꺼짐"); }
        StopAllCoroutines();
        SequenceRunning = false;
        if (Arm != null) { Arm.StatusChanged -= OnArmStatus; }
    }


    //  ============================================================
    //  공정 (CellController가 부른다)
    //  ============================================================
    // 상자의 face 면이 위로 오게 교정하고 끝날 때까지 기다린다. 결과는 LastSuccess · FailReason.
    /// [호출] CellController.JudgeAndCorrect(): yield return correction.CorrectAt(box, face);
    /// targetBox = 상자, faceToRaise = face → 물리 켜기 → RaiseSelectedFaceAndPlace() → 끝날 때까지 대기 → 상자가 멈추면 결과 기록.
    /// Top · None이면 돌리지 않고 성공. 실패하면 box.Mark(CorrectionFail)로 표시한다 (판독 구간을 거쳐 NG 토트로 간다).
    public IEnumerator CorrectAt(BoxInfo box, BoxFace face)
    {
        while (SequenceRunning) { yield return null; }
        float start = Time.time;
        failed = false;
        FailReason = null;
        LastSuccess = false;

        //  1) 올릴 면이 위 (또는 없음)이면 돌리지 않는다
        if (face == BoxFace.Top || face == BoxFace.None)
        {
            LastSuccess = true;
            yield break;
        }

        //  2) 상자를 연결한다
        Rigidbody body = box != null ? box.GetComponent<Rigidbody>() : null;
        targetBox = box != null ? box.GetComponent<BoxCollider>() : null;
        if (targetBox == null || body == null)
        {
            Fail("상자에 BoxCollider 또는 Rigidbody가 없음");
            if (box != null) { box.Mark(ExceptionReason.CorrectionFail); }
            yield break;
        }
        faceToRaise = face;
        Debug.Log($"[Correct] {box.boxId} 교정 시작 | {face} 면을 위로", this);

        //  3) 물리를 켠다 (벨트 위 상자는 kinematic). 벨트 위에 자리 잡을 때까지 잠깐 기다린다
        body.isKinematic = false;
        body.useGravity = true;
        yield return new WaitForSeconds(0.3f);

        //  4) 면 올리기 순서를 시작하고 끝날 때까지 기다린다
        RaiseSelectedFaceAndPlace();
        while (SequenceRunning) { yield return null; }

        //  5) 내려놓은 상자가 멈출 때까지 기다린다 (최대 1.5초)
        yield return WaitBoxSettle(body);

        //  6) 결과 기록. 실패면 교정 실패로 표시한다
        LastSuccess = !failed;
        if (LastSuccess)
        { Debug.Log($"[Correct] {box.boxId} 교정 끝 | 90° {CompletedQuarterTurns}번 | {Time.time - start:0.0}초", this); }
        else
        {
            box.Mark(ExceptionReason.CorrectionFail);
            Debug.LogWarning($"[Correct] {box.boxId} 교정 실패 | {FailReason} | {Time.time - start:0.0}초", this);
        }
    }


    //  ============================================================
    //  면 올리기 순서 (코루틴)
    //  ============================================================
    // faceToRaise가 위로 올 때까지 90° 돌리기를 최대 2번 한다 (코루틴 시작). 이미 도는 중이거나 칸이 비었으면 시작하지 않는다.
    /// [호출] CorrectAt(), 테스트 메뉴
    /// [!] 상자 물리가 켜져 있어야 한다 (kinematic이면 손가락으로 들 수 없다). 공정에서는 CorrectAt()이 켠다.
    public void RaiseSelectedFaceAndPlace()
    {
        if (SequenceRunning) { return; }
        if (faceToRaise == BoxFace.Top || faceToRaise == BoxFace.None)
        {
            SetStatus("올릴 면이 위 · 없음이라 돌리지 않습니다.");
            return;
        }

        failed = false;
        FailReason = null;
        string problem = !Application.isPlaying ? "Play 중이 아님"
                       : tcp == null ? "Tcp 칸 비어 있음"
                       : gripper == null ? "Gripper 칸 비어 있음"
                       : targetBox == null ? "Target Box 칸 비어 있음"
                       : targetBox.attachedRigidbody == null || targetBox.attachedRigidbody.isKinematic ? "상자 물리가 꺼져 있음 (kinematic)"
                       : initialJointAngles == null || initialJointAngles.Length != 6 ? "준비 자세 관절각 없음"
                       : null;
        if (problem != null)
        {
            Fail(problem);
            return;
        }

        SequenceRunning = true;
        StartCoroutine(FaceRaiseRoutine());
    }

    // 면 올리기: (90° 1번) × 최대 2. 실패하면 정리한 뒤, 성공 · 실패 모두 준비 자세로 돌아간다.
    IEnumerator FaceRaiseRoutine()
    {
        ConfigureArm();
        BindFace();
        CurrentRotationStage = 0;
        CompletedQuarterTurns = 0;

        for (int stage = 1; stage <= 2; stage++)
        {
            if (IsFaceUp(raiseLocal)) { break; }

            CurrentRotationStage = stage;
            PrepareStage();
            yield return QuarterTurn();
            if (failed) { break; }
            CompletedQuarterTurns = stage;

            //  11) 아직 위로 안 왔으면 준비 자세로 갔다가 다시 집는다
            if (!IsFaceUp(raiseLocal)) { yield return GoHome(); }
        }

        //  확인: 올릴 면이 위로 왔는지
        if (!failed && !IsFaceUp(raiseLocal))
        { Fail($"{faceToRaise} 면이 위로 오지 않음 (기울기 {FaceTilt(raiseLocal):0}°)"); }

        if (failed) { yield return Recover(); }
        yield return GoHome();

        LastSuccess = !failed;
        CycleStep = failed ? "실패" : "완료";
        SequenceRunning = false;
    }

    // 90° 한 번. 한 단계라도 실패하면 Fail()을 남기고 바로 끝낸다.
    IEnumerator QuarterTurn()
    {
        //  1) 손을 연다
        CycleStep = "열기";
        OpenGripper();
        yield return new WaitForSeconds(0.4f);

        //  2) 상자 옆 대기 위치로 간다 (세울 면 쪽)
        CycleStep = "옆면 접근";
        ApproachBox();
        yield return WaitArm();
        if (failed) { yield break; }

        //  3) 잡기 위치로 곧게 들어간다
        CycleStep = "잡기 위치";
        DescendToBox();
        yield return WaitArm();
        if (failed) { yield break; }

        //  4) 두 손가락이 같은 높이인지 (닫히는 축이 수평인지) 확인
        if (!FingersLevel(out string why))
        {
            Fail(why);
            yield break;
        }

        //  5) closeTime 동안 천천히 닫고, 손가락이 자리 잡을 때까지 기다린다
        CycleStep = "닫기";
        yield return MoveFingers(GripCloseForBox(), closeTime);
        yield return new WaitForSeconds(0.8f);

        //  6) 들어 올린다. 상자 바닥이 liftCheck 이상 올라와야 잡은 것
        CycleStep = "들어 올리기";
        float bottom = targetBox.bounds.min.y;
        Lift();
        yield return WaitArm();
        if (failed) { yield break; }
        yield return new WaitForSeconds(0.4f);
        float risen = targetBox.bounds.min.y - bottom;
        if (risen < liftCheck)
        {
            Fail($"잡기 실패 · 상자가 {risen * 100f:0.0} cm만 올라옴 (기준 {liftCheck * 100f:0.0} cm)");
            yield break;
        }

        //  7) 90° 돌린다 (세울 면을 위로). 손 안에서 미끄러졌는지 각도로 확인한다
        CycleStep = "회전";
        TurnHeldParcel90();
        yield return WaitArm();
        if (failed) { yield break; }
        yield return new WaitForSeconds(0.4f);
        if (FaceTilt(stageLocal) >= 10f)
        {
            Fail($"회전 각도 부족 · 세울 면이 위와 {FaceTilt(stageLocal):0}° 어긋남");
            yield break;
        }

        //  8) 처음 자리 위 벨트로 내려놓는다
        CycleStep = "내려놓기";
        PlaceTurnedParcel();
        yield return WaitArm();
        if (failed) { yield break; }
        yield return new WaitForSeconds(0.8f);

        //  9) 손을 천천히 연다
        CycleStep = "놓기";
        yield return MoveFingers(0f, 0.8f);
        gripper.Open();
        yield return new WaitForSeconds(1f);

        //  10) 위로 빠진다
        CycleStep = "물러나기";
        Lift();
        yield return WaitArm();
    }

    // 실패 뒤 정리: 들어 올린 상자는 벨트 바로 위까지 내리고, 손을 열고, 상자 옆이면 위로 빠진다.
    /// 각 단계가 안 되면 건너뛴다. 실패 사유는 처음 것만 남으므로 여기서 생긴 문제는 사유를 덮지 않는다.
    IEnumerator Recover()
    {
        CycleStep = "실패 정리";
        if (Arm.Moving) { Arm.StopMotion(); }

        //  1) 들어 올린 채면 바닥이 벨트 4 mm 위가 될 때까지 곧게 내린다
        bool closed = gripper != null && gripper.closeAmount > 0.001f;
        if (closed && targetBox != null && FindBelt(out float surfaceY) && targetBox.bounds.min.y > surfaceY + 0.01f)
        {
            Vector3 down = tcp.position + Vector3.up * (surfaceY + 0.004f - targetBox.bounds.min.y);
            stepStarted = Arm.MoveTo(down, tcp.rotation);
            yield return WaitArm(false);
        }

        //  2) 손을 연다
        if (gripper != null) { yield return MoveFingers(0f, 0.5f); }
        yield return new WaitForSeconds(0.5f);

        //  3) 손끝이 상자에서 5 cm 안이면 곧게 올라간다 (준비 자세 경로가 상자 간격 검사에 걸리지 않게)
        if (targetBox != null && targetBox.bounds.SqrDistance(tcp.position) < 0.05f * 0.05f)
        {
            Lift();
            yield return WaitArm(false);
        }
    }

    // 준비 자세로 돌아간다. 가지 못해도 교정 결과는 바꾸지 않고 경고만 남긴다 (상자 교정은 이미 끝났으므로).
    IEnumerator GoHome()
    {
        CycleStep = "준비 자세 복귀";
        if (!ReturnToReady())
        {
            Debug.LogWarning($"[Correct] {name}: 준비 자세로 돌아가지 못했습니다 · {status}", this);
            yield break;
        }
        stepStarted = true;
        yield return WaitArm(false);
        if (!armArrived)
        { Debug.LogWarning($"[Correct] {name}: 준비 자세에 도착하지 못했습니다 · {status}", this); }
    }

    // 단계 동작이 시작한 이동이 끝날 때까지 기다린다. 결과는 armArrived.
    /// required가 true면 이동을 시작하지 못했거나, 도착하지 못했거나, motionTimeout을 넘었을 때 지금 단계 이름으로 실패를 남긴다.
    IEnumerator WaitArm(bool required = true)
    {
        armArrived = false;
        if (!stepStarted)
        {
            if (required) { Fail($"{CycleStep} 시작 못 함 · {status}"); }
            yield break;
        }

        float start = Time.time;
        while (Arm.Moving)
        {
            if (Time.time - start > motionTimeout)
            {
                Arm.StopMotion();
                if (required) { Fail($"{CycleStep} 시간 초과 ({motionTimeout:0}초)"); }
                yield break;
            }
            yield return null;
        }

        armArrived = Arm.Arrived;
        if (!armArrived && required) { Fail($"{CycleStep} 도착 실패 · {Arm.Status}"); }
    }

    // 손가락을 duration 동안 target까지 천천히 움직인다.
    IEnumerator MoveFingers(float target, float duration)
    {
        float from = gripper.closeAmount;
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            gripper.closeAmount = Mathf.Lerp(from, target, time / duration);
            yield return null;
        }
        gripper.closeAmount = target;
    }

    // 내려놓은 상자가 멈출 때까지 기다린다 (최대 1.5초).
    IEnumerator WaitBoxSettle(Rigidbody body)
    {
        yield return new WaitForSeconds(0.3f);

        float start = Time.time;
        while (body != null && !body.isKinematic && Time.time - start < 1.5f)
        {
            if (body.linearVelocity.sqrMagnitude < 0.0001f && body.angularVelocity.sqrMagnitude < 0.01f) { break; }
            yield return new WaitForFixedUpdate();
        }
    }

    // 실패를 기록한다. 사유는 처음 것만 남긴다 (뒤에 생긴 문제가 원인을 덮지 않게).
    void Fail(string reason)
    {
        if (!failed)
        {
            failed = true;
            FailReason = reason;
            Debug.LogWarning($"[Correct] {name}: {CycleStep} | {reason}", this);
        }
        SetStatus(reason);
    }


    //  ============================================================
    //  단계 동작 (Inspector 버튼 · 에디터 도구 · 면 올리기 순서가 부른다)
    //  ============================================================
    // 1번 버튼. 상자 옆 대기 위치로 간다 (세울 면 쪽, 30° 아래를 보는 자세). 끝났는지는 Moving으로 본다.
    /// 손목 방향 2가지 × 거리 0.15 · 0.10 · 0.07 m × 올라가는 높이 4가지 중 처음 계획되는 경로를 쓴다. 고른 손목 방향 · 거리는 DescendToBox()가 이어 쓴다.
    /// 경로: ① 지금 자리에서 올라가기 → ② 손목 돌리기 → ③ 대기 위치 위로 옮기기 → ④ 내려가기 (모든 중간 자세가 상자와 boxClearance 이상 떨어져야 한다).
    /// 모두 안 되면 관절을 한 번에 돌리는 경로를 쓴다 (ArmIK.MoveToJointPath).
    /// [!] 순서 밖(버튼)에서 부르면 faceToRaise로 세울 면을 정한다.
    public void ApproachBox()
    {
        stepStarted = false;
        if (!CanMove(true)) { return; }
        if (!SequenceRunning)
        {
            BindFace();
            PrepareStage();
        }

        foreach (float sign in new[] { 1f, -1f })
        {
            foreach (float distance in new[] { 0.15f, 0.10f, 0.07f })
            {
                closingSign = sign;
                standOff = distance;
                if (!GetBoxGoal(approachHeight, out Vector3 position, out Quaternion desired)) { return; }

                foreach (float extra in new[] { 0f, travelClearance, travelClearance * 0.65f, travelClearance * 1.4f })
                {
                    float high = Mathf.Max(tcp.position.y, position.y) + extra;
                    Vector3 aboveStart = new Vector3(tcp.position.x, high, tcp.position.z);
                    Vector3 aboveBox = new Vector3(position.x, high, position.z);
                    Vector3[] points = { aboveStart, aboveStart, aboveBox, position };
                    Quaternion[] rotations = { tcp.rotation, desired, desired, desired };
                    if (Arm.MoveThrough(points, rotations, true))
                    {
                        stepStarted = true;
                        return;
                    }
                }

                if (Arm.MoveToJointPath(position, desired, targetBox))
                {
                    stepStarted = true;
                    return;
                }
            }
        }
        SetStatus("상자와 부딪히지 않는 접근 경로가 없습니다. 상자 위치나 준비 자세를 확인하세요.");
    }

    // 2번 버튼. 옆 대기 위치에서 잡기 위치(손끝 = 상자 중심 5 mm 위)로 곧게 들어간다.
    /// [!] 먼저 ApproachBox()로 대기 위치에 와 있어야 한다 (1.5 cm · 6° 안).
    public void DescendToBox()
    {
        stepStarted = false;
        if (!CanMove(true)) { return; }
        if (!GetBoxGoal(approachHeight, out Vector3 waitPosition, out Quaternion waitRotation)) { return; }
        if (!GetBoxGoal(0f, out Vector3 position, out Quaternion desired)) { return; }

        if (Vector3.Distance(tcp.position, waitPosition) > 0.015f || Quaternion.Angle(tcp.rotation, waitRotation) > 6f)
        {
            SetStatus("먼저 상자 옆 대기 위치로 이동하세요 (1번 버튼).");
            return;
        }
        stepStarted = Arm.MoveTo(position, desired);
    }

    // 3번 버튼. 지금 자세 그대로 liftHeight만큼 곧게 올린다 (들어 올리기, 놓은 뒤 물러나기).
    public void Lift()
    {
        stepStarted = false;
        if (!CanMove(false)) { return; }
        stepStarted = Arm.MoveTo(tcp.position + Vector3.up * liftHeight, tcp.rotation);
    }

    // 들고 있는 상자를 90° 돌린다 (세울 면을 위로). 돌린 뒤 벨트까지 내려놓을 수 있는 경로인지 먼저 확인한다.
    /// 더 올릴 높이 0.10 · 0.15 · 0.06 m × 로봇 쪽으로 당길 거리 0 · ±0.08 · ±0.16 m 중 처음 되는 조합을 쓴다.
    /// 상자는 손끝(tcp)을 중심으로 돈다. 손끝이 상자 중심에 있으므로 상자는 거의 제자리에서 돈다.
    public void TurnHeldParcel90()
    {
        stepStarted = false;
        if (!CanMove(true)) { return; }
        if (!hasStage || gripper == null || gripper.closeAmount <= 0.001f)
        {
            SetStatus("먼저 상자를 잡고 들어 올리세요 (1 → 2 → 그리퍼 닫기 → 3).");
            return;
        }
        if (!FindBelt(out float surfaceY))
        {
            SetStatus("상자 아래 벨트 면을 찾지 못했습니다. Placement Surface를 연결하세요.");
            return;
        }

        Quaternion original = tcp.rotation;
        Quaternion turn = Quaternion.FromToRotation(StageNormal(), Vector3.up);
        Quaternion turned = turn * original;
        Vector3 inward = Horizontal(transform.position - tcp.position).normalized;

        foreach (float height in new[] { 0.10f, 0.15f, 0.06f })
        {
            foreach (float offset in new[] { 0f, 0.08f, -0.08f, 0.16f, -0.16f })
            {
                Vector3 raised = tcp.position + Vector3.up * height + inward * offset;

                //  돌린 뒤 놓을 위치: 바닥이 벨트 2 mm 위, 중심은 처음 자리 위
                Vector3 turnedCentre = raised + turn * (targetBox.bounds.center - tcp.position);
                Vector3 placed = raised + Vector3.up * (surfaceY + 0.002f - PredictedBottom(raised, turned));
                placed += Horizontal(placementAnchor - turnedCentre);

                //  ① 올리기 · 당기기 → ② 돌리기 → ③ 내려놓기까지 되는지 확인하고, 지금은 ②까지만 간다
                if (!Arm.CanMoveThrough(new[] { raised, raised, placed }, new[] { original, turned, turned }, false)) { continue; }
                if (Arm.MoveThrough(new[] { raised, raised }, new[] { original, turned }, false))
                {
                    stepStarted = true;
                    return;
                }
            }
        }
        SetStatus("90° 돌린 뒤 내려놓을 수 있는 경로가 없습니다.");
    }

    // 돌린 상자를 처음 자리 위 벨트로 곧게 내린다 (바닥이 벨트 2 mm 위). 손은 열지 않는다.
    public void PlaceTurnedParcel()
    {
        stepStarted = false;
        if (!CanMove(true)) { return; }
        if (!FindBelt(out float surfaceY))
        {
            SetStatus("상자 아래 벨트 면을 찾지 못했습니다. Placement Surface를 연결하세요.");
            return;
        }

        Bounds bounds = targetBox.bounds;
        Vector3 position = tcp.position + Vector3.up * (surfaceY + 0.002f - bounds.min.y) + Horizontal(placementAnchor - bounds.center);
        stepStarted = Arm.MoveTo(position, tcp.rotation);
    }

    // 준비 자세로 가는 경로를 찾아 이동을 시작한다. 경로가 없으면 false.
    /// 관절을 한 번에 돌리는 경로다. Target Box가 있으면 모든 중간 자세가 상자와 떨어져 있어야 한다 (상자 옆이면 먼저 Lift()).
    public bool ReturnToReady()
    {
        if (!CanMove(false)) { return false; }
        if (initialJointAngles == null || initialJointAngles.Length != 6)
        {
            SetStatus("준비 자세 관절각이 없습니다.");
            return false;
        }
        return Arm.MoveToJointAngles(initialJointAngles, targetBox);
    }

    // 순서를 끊고 지금 관절각을 유지한다. 손가락은 그대로 둔다.
    /// [호출] CellController 알람 정지, Inspector 정지 버튼
    public void Stop()
    {
        if (SequenceRunning) { Fail("정지 명령"); }
        StopAllCoroutines();
        SequenceRunning = false;
        if (Arm != null) { Arm.StopMotion(); }
        CycleStep = "정지";
    }

    // 손을 연다.
    public void OpenGripper()
    {
        if (gripper != null) { gripper.Open(); }
    }

    // 상자 폭에 맞춘 닫힘량으로 바로 닫는다 (Inspector 버튼). 면 올리기 순서는 closeTime 동안 천천히 닫는다.
    public void CloseGripper()
    {
        if (gripper != null) { gripper.closeAmount = GripCloseForBox(); }
    }

    // 준비 자세 관절각을 바꾼다.
    /// [호출] 에디터 도구 (Set Raised Ready Pose, Arrange Robot Pick Station)
    public void SetInitialJointAngles(float[] values)
    {
        initialJointAngles = (float[])values.Clone();
    }


    //  ============================================================
    //  계산 도우미
    //  ============================================================
    // 손끝 목표 위치 · 자세. height > 0이면 대기 위치, 0이면 잡기 위치. 상자가 없거나 그리퍼를 못 찾으면 false.
    /// 옆면 집기: 세울 면 쪽에서 30° 아래를 보며 들어가고, 손가락은 세울 면과 위쪽에 모두 수직인 수평 축으로 닫는다.
    ///            세울 면이 정해지지 않았으면(에디터 도구) 로봇 쪽 옆면을 쓴다.
    /// 윗면 집기: 위에서 곧게 내려가고, 손가락은 상자의 짧은 수평 변 방향으로 닫는다.
    /// [호출] ApproachBox(), DescendToBox(), 에디터 도구 (ArrangeParcelIKStation, SetRobotReadyPose, Configure Parcel Arm IK)
    public bool GetBoxGoal(float height, out Vector3 position, out Quaternion desired)
    {
        position = Vector3.zero;
        desired = Quaternion.identity;
        if (targetBox == null)
        {
            SetStatus("Target Box에 상자를 지정하세요.");
            return false;
        }

        Transform gripBase = GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "gripper_base_link");
        Vector3 jaw = JawClosingDirection();
        if (tcp == null || gripBase == null || jaw == Vector3.zero)
        {
            SetStatus("Tcp · gripper_base_link · finger_left_link를 찾지 못했습니다.");
            return false;
        }

        //  그리퍼 기준 자세: 손가락이 향한 쪽(approach)과 닫히는 축(closing)을 tcp 로컬로 잰다
        Vector3 localApproach = tcp.InverseTransformDirection(tcp.position - gripBase.position).normalized;
        Vector3 localClosing = tcp.InverseTransformDirection(jaw).normalized;
        Quaternion localFrame = Quaternion.LookRotation(Vector3.Cross(localClosing, localApproach), localApproach);
        Transform boxTransform = targetBox.transform;
        Bounds bounds = targetBox.bounds;

        if (sideGrasp)
        {
            float upright = Mathf.Max(Mathf.Abs(boxTransform.right.y), Mathf.Abs(boxTransform.up.y), Mathf.Abs(boxTransform.forward.y));
            if (upright < Mathf.Cos(15f * Mathf.Deg2Rad))
            {
                SetStatus("상자가 벨트에 평평하게 놓여 있지 않습니다.");
                return false;
            }

            Vector3 normal = Horizontal(StageNormal());
            if (normal.sqrMagnitude < 0.25f) { normal = Horizontal(transform.position - bounds.center); }
            normal.Normalize();

            Vector3 approach = -normal;
            Vector3 closing = Vector3.Cross(approach, Vector3.up).normalized * closingSign;
            approach = (approach * 0.8660254f + Vector3.down * 0.5f).normalized;          //  30° 아래로 기울인다
            desired = Quaternion.LookRotation(Vector3.Cross(closing, approach), approach) * Quaternion.Inverse(localFrame);

            float extent = Mathf.Abs(approach.x) * bounds.extents.x + Mathf.Abs(approach.z) * bounds.extents.z;
            position = bounds.center + Vector3.up * 0.005f;
            if (height > 0f) { position -= approach * (extent + Mathf.Min(height, standOff)); }
        }
        else
        {
            Vector3 closing = Horizontal(boxTransform.right).normalized;
            Vector3 other = Horizontal(boxTransform.forward).normalized;
            float sizeX = boxTransform.TransformVector(Vector3.right * targetBox.size.x).magnitude;
            float sizeZ = boxTransform.TransformVector(Vector3.forward * targetBox.size.z).magnitude;
            if (sizeZ < sizeX && other.sqrMagnitude > 0.9f) { closing = other; }
            if (closing.sqrMagnitude < 0.9f) { closing = Vector3.right; }

            desired = Quaternion.LookRotation(Vector3.Cross(closing, Vector3.down), Vector3.down) * Quaternion.Inverse(localFrame);
            position = bounds.center + Vector3.up * (height > 0f ? bounds.extents.y + height : 0f);
        }
        return true;
    }

    // 손가락이 닫히는 방향 (월드). finger_left_link가 없으면 zero.
    /// URDF 손가락 관절은 anchor가 돌아가 있어 손가락 메시의 X축이 닫히는 축이 아니다. 관절 anchor 기준으로 잰다.
    public Vector3 JawClosingDirection()
    {
        ArticulationBody finger = FindFinger("finger_left_link");
        if (finger == null) { return Vector3.zero; }
        return finger.transform.TransformDirection(finger.anchorRotation * Vector3.right).normalized;
    }

    // 상자 폭에 맞춘 손가락 닫힘량. 폭 6 cm면 parcelGripCloseAmount, 폭이 1 cm 좁으면 손가락마다 5 mm 더 닫는다.
    public float GripCloseForBox()
    {
        Vector3 closing = JawClosingDirection();
        if (targetBox == null || closing == Vector3.zero) { return parcelGripCloseAmount; }

        Transform boxTransform = targetBox.transform;
        float width = Mathf.Abs(Vector3.Dot(boxTransform.TransformVector(Vector3.right * targetBox.size.x), closing))
                    + Mathf.Abs(Vector3.Dot(boxTransform.TransformVector(Vector3.up * targetBox.size.y), closing))
                    + Mathf.Abs(Vector3.Dot(boxTransform.TransformVector(Vector3.forward * targetBox.size.z), closing));
        return Mathf.Clamp(parcelGripCloseAmount + (0.06f - width) * 0.5f, 0.003f, 0.04f);
    }

    // 두 손가락이 같은 높이인지 (닫히는 축이 수평인지). 아니면 why에 이유.
    /// 허용: 기울기 (angleTolerance + 1.5°, 2 ~ 8°), 높이 차이 max(8 mm, 손가락 간격 × sin(기울기) + 2 mm).
    bool FingersLevel(out string why)
    {
        why = null;
        if (!FingerCentre(FindFinger("finger_left_link"), out Vector3 left) || !FingerCentre(FindFinger("finger_right_link"), out Vector3 right))
        {
            why = "잡기 중단 · 손가락 Collider를 찾지 못함";
            return false;
        }

        float tiltLimit = Mathf.Sin(Mathf.Clamp(angleTolerance + 1.5f, 2f, 8f) * Mathf.Deg2Rad);
        float tolerance = Mathf.Max(0.008f, Vector3.Distance(left, right) * tiltLimit + 0.002f);
        float difference = Mathf.Abs(left.y - right.y);
        if (Mathf.Abs(Vector3.Dot(JawClosingDirection(), Vector3.up)) > tiltLimit || difference > tolerance)
        {
            why = $"잡기 중단 · 손가락 높이 차이 {difference * 1000f:0.0} mm (허용 {tolerance * 1000f:0.0} mm)";
            return false;
        }
        return true;
    }

    // 손가락 Collider 전체의 중심. Collider가 없으면 false.
    static bool FingerCentre(ArticulationBody finger, out Vector3 centre)
    {
        centre = Vector3.zero;
        if (finger == null) { return false; }

        Collider[] colliders = finger.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
        if (colliders.Length == 0) { return false; }

        Bounds bounds = colliders[0].bounds;
        foreach (Collider other in colliders.Skip(1)) { bounds.Encapsulate(other.bounds); }
        centre = bounds.center;
        return true;
    }

    ArticulationBody FindFinger(string fingerName)
    {
        return GetComponentsInChildren<ArticulationBody>(true).FirstOrDefault(body => body.name == fingerName);
    }

    // 상자 아래 벨트 윗면 높이. placementSurface가 있으면 그 면만, 없으면 이름에 "Conveyor"가 들어간 오브젝트(부모 포함)를 본다.
    bool FindBelt(out float surfaceY)
    {
        surfaceY = float.NegativeInfinity;
        if (targetBox == null) { return false; }

        Bounds bounds = targetBox.bounds;
        foreach (RaycastHit hit in Physics.RaycastAll(bounds.center + Vector3.up * 0.1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == targetBox || hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(targetBox.transform)) { continue; }
            if (!IsBelt(hit.collider)) { continue; }
            if (hit.point.y <= bounds.min.y + 0.01f) { surfaceY = Mathf.Max(surfaceY, hit.point.y); }
        }
        return !float.IsNegativeInfinity(surfaceY);
    }

    //  현재 아래 지탱하는 평면이 벨트가 맞는지 판단
    bool IsBelt(Collider surface)
    {
        if (placementSurface != null) { return surface == placementSurface; }

        for (Transform t = surface.transform; t != null; t = t.parent)
            if (t.name.IndexOf("Conveyor", System.StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
        return false;
    }

    // 손끝을 position · rotation으로 옮겼을 때 (상자를 손에 쥔 채로 본) 상자 바닥 높이.
    float PredictedBottom(Vector3 position, Quaternion rotation)
    {
        float bottom = float.PositiveInfinity;
        Quaternion delta = rotation * Quaternion.Inverse(tcp.rotation);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = targetBox.center + Vector3.Scale(targetBox.size * 0.5f,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            bottom = Mathf.Min(bottom, (position + delta * (targetBox.transform.TransformPoint(corner) - tcp.position)).y);
        }
        return bottom;
    }

    // 올릴 면(faceToRaise)을 상자 로컬 축으로 기억하고, 내려놓을 자리(지금 상자 중심)를 정한다.
    void BindFace()
    {
        raiseLocal = NearestLocalAxis(BoxInfo.FaceDirection(faceToRaise));
        placementAnchor = targetBox.bounds.center;
    }

    // 이번 90°에 세울 면을 정한다. 올릴 면이 옆을 보면 그 면, 바닥을 보면 로봇 쪽 옆면.
    /// 로봇 쪽 옆면을 세우면 바닥면이 로봇 쪽을 보게 되어, 두 번째도 로봇 쪽에서 집는다.
    void PrepareStage()
    {
        Vector3 raiseNow = targetBox.transform.TransformDirection(raiseLocal);
        if (Vector3.Dot(raiseNow, Vector3.down) > 0.8f)
        { stageLocal = NearestLocalAxis(Horizontal(transform.position - targetBox.bounds.center).normalized); }
        else
        { stageLocal = raiseLocal; }
        hasStage = true;
    }

    // 세울 면의 월드 방향. 정해지지 않았으면 zero (GetBoxGoal()이 로봇 쪽으로 대신한다).
    Vector3 StageNormal()
    {
        if (!hasStage || targetBox == null) { return Vector3.zero; }
        return targetBox.transform.TransformDirection(stageLocal).normalized;
    }

    // worldDirection에 가장 가까운 상자 로컬 축 (±X, ±Y, ±Z 중 하나).
    Vector3 NearestLocalAxis(Vector3 worldDirection)
    {
        Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        return axes.OrderByDescending(axis => Vector3.Dot(targetBox.transform.TransformDirection(axis), worldDirection)).First();
    }

    // 상자 로컬 면이 위쪽과 이루는 각도 °. 10° 안이면 위를 향한 것으로 본다.
    float FaceTilt(Vector3 local) => Vector3.Angle(targetBox.transform.TransformDirection(local), Vector3.up);
    bool IsFaceUp(Vector3 local) => FaceTilt(local) <= 10f;

    static Vector3 Horizontal(Vector3 vector) => new Vector3(vector.x, 0f, vector.z);

    // 움직일 수 있는 상태인지 확인하고 ArmIK에 지금 값을 넘긴다. 다른 이동이 있으면 멈춘다.
    bool CanMove(bool needBox)
    {
        if (!Application.isPlaying)
        {
            SetStatus("Play 모드에서 실행하세요.");
            return false;
        }
        if (tcp == null || Arm == null)
        {
            SetStatus("Tcp 칸이 비어 있거나 ArmIK가 없습니다.");
            return false;
        }
        if (needBox && targetBox == null)
        {
            SetStatus("Target Box에 상자를 지정하세요.");
            return false;
        }

        ConfigureArm();
        if (Arm.Moving) { Arm.StopMotion(); }
        return true;
    }

    // Inspector 값을 ArmIK에 넘긴다 (tcp, 피할 상자, 속도, 허용 오차, 상자 간격).
    void ConfigureArm()
    {
        if (Arm == null) { return; }
        Arm.Configure(tcp, targetBox, degreesPerSecond, metresPerSecond, positionTolerance, angleTolerance, boxClearance, compensateRobotGravity);
    }

    void SetStatus(string text) { status = text; }
    void OnArmStatus(string text) { status = text; }


    //  ============================================================
    //  테스트 (CorrectionSequence ⋮ 메뉴 · 테스트 패널, Play 중에만)
    //  ============================================================
    // [테스트] ⋮ → "테스트/교정 1회 (송장 면)": Test Line 정지 위치의 상자(없으면 Target Box)를 실제 송장 면(LabelFaceNow)이 위로 오게 교정한다.
    /// 공정과 같은 CorrectAt()을 쓴다. 끝나면 상자는 물리가 켜진 채 남는다 (ConveyorLine의 "정지 상자 보내기"가 끈다).
    [ContextMenu("테스트/교정 1회 (송장 면)")]
    void TestCorrectOnce()
    {
        if (!Application.isPlaying || SequenceRunning) { return; }

        BoxInfo box = testLine != null && testLine.HasBoxAtStop ? testLine.CurrentBox
                    : targetBox != null ? targetBox.GetComponent<BoxInfo>()
                    : null;
        if (box == null)
        {
            Debug.LogWarning($"[Correct] {name}: 교정할 상자가 없습니다. Test Line 정지 위치에 상자를 세우거나 Target Box를 넣으세요.", this);
            return;
        }
        StartCoroutine(CorrectAt(box, box.LabelFaceNow));
    }

    // [테스트] ⋮ → "테스트/면 올리기 (Face To Raise)": Target Box의 faceToRaise 면을 올린다 (상자 물리가 켜져 있어야 한다).
    [ContextMenu("테스트/면 올리기 (Face To Raise)")]
    void TestRaiseSelectedFace()
    {
        if (!Application.isPlaying) { return; }
        RaiseSelectedFaceAndPlace();
    }

    // [테스트] ⋮ → "테스트/준비 자세로"
    [ContextMenu("테스트/준비 자세로")]
    void TestReturnToReady()
    {
        if (!Application.isPlaying || SequenceRunning) { return; }
        ReturnToReady();
    }

    // 테스트 패널 (showTestPanel이 켜져 있을 때만)
    void OnGUI()
    {
        if (!showTestPanel) { return; }

        GUILayout.BeginArea(new Rect(guiPosition.x, guiPosition.y, 300, 150), GUI.skin.box);
        GUILayout.Label($"[교정 로봇] {(SequenceRunning ? $"{CurrentRotationStage}번째 90° · {CycleStep}" : CycleStep)}");
        GUILayout.Label(SequenceRunning ? status : $"마지막 결과: {(LastSuccess ? "성공" : FailReason ?? "-")}");
        GUI.enabled = !SequenceRunning;
        if (GUILayout.Button("교정 1회 (송장 면)")) { TestCorrectOnce(); }
        if (GUILayout.Button("준비 자세로")) { TestReturnToReady(); }
        GUI.enabled = true;
        if (GUILayout.Button("정지")) { Stop(); }
        GUILayout.EndArea();
    }

    // Scene 뷰에서 선택하면 상자 경계(노랑), 세울 면 방향(자홍), 이동 목표(하늘)를 그린다.
    void OnDrawGizmosSelected()
    {
        if (targetBox != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(targetBox.bounds.center, targetBox.bounds.size);

            Vector3 normal = StageNormal();
            if (normal != Vector3.zero)
            {
                Vector3 centre = targetBox.bounds.center;
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(centre, centre + normal * 0.14f);
                Gizmos.DrawSphere(centre + normal * 0.14f, 0.006f);
            }
        }

        if (Moving && tcp != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(GoalPosition, 0.015f);
            Gizmos.DrawLine(tcp.position, GoalPosition);
        }
    }
}
