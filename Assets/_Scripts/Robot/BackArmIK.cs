using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

//  ============================================================
//  BackArmIK
//  흡착 로봇(fr5_suction)의 이동 담당. 목표 위치 · 회전까지 관절 경로를 만들고, 관절 구동 목표를 조금씩 옮겨 팔이 따라가게 한다.
//      - 계산(순운동학 · 역운동학 · 겹침 검사)은 ArmKinematics가 한다. 이 스크립트는 경로 만들기와 실행을 맡는다.
//      - 상자 작업 순서는 없다. BoxAttacher가 이동 명령을 내리고, Moving · Status로 끝을 확인한다.
//      - 교정 로봇(fr5_with_gripper)은 FrontArmIK를 쓴다. 두 스크립트는 흡착 전용 함수를 빼면 같은 구조다.
//
//  [이동 명령]
//      MoveTo()                지금 자세에서 목표까지 손끝을 곧게 옮긴다
//      MoveToJointPath()       목표 자세의 관절각을 먼저 구하고, 관절각을 고르게 바꿔 간다 (손끝 경로는 곡선)
//      MoveToJointAngles()     정해 둔 관절각(준비 자세 등)으로 간다
//  [흡착 경로] 옆면(X · Y 방향) 흡착용. 경로를 모두 미리 계산해 두고, 확인된 경로 그대로 나눠 실행한다 (실행 중에 다른 관절 해로 바뀌지 않게).
//      PlanSuctionApproach()           접촉 자세를 먼저 풀고, 거꾸로 물러나며 대기 위치 → 접근 → 접촉 경로를 만든다 (preview = true면 계획만)
//      PreviewSuctionRotation()        접촉 뒤 들기 → 회전 대기 위치로 옮기기 → 나눠 돌리기 경로를 미리 계산해 저장한다
//      StartPreparedSuctionApproach()  → (흡착) → StartPreparedSuctionLift() → StartPreparedSuctionTransfer() → StartPreparedSuctionTurn()
//
//  [사용] fr5_suction 루트에 BoxAttacher와 함께 붙는다 (BoxAttacher의 RequireComponent). BoxAttacher가 Configure() → Initialize(준비 자세)를 부른다.
//         이동 명령이 true를 돌려주면 이동이 시작된 것이다. Moving이 false가 되면 Status로 결과를 본다.
//  [!] MoveTo()만 지금 이동을 멈추고 새로 시작한다. 다른 이동 명령은 움직이는 중이면 false.
//  [!] 도착 여부 값은 따로 없다. BoxAttacher.Arrived가 Status 앞부분 "목표 도착"으로 판단한다.
//  [!] 60초 안에 도착하지 못하면 멈춘다. 보호할 상자가 움직이거나 팔이 상자에 너무 가까워져도 멈춘다.
//  [!] Initialize()는 URDF Importer의 키보드 조종 스크립트(Controller · JointControl)를 끈다. 꺼질 때 다시 켠다.
//  [!] 한 로봇에 FrontArmIK와 BackArmIK를 같이 붙이지 않는다. 둘 다 같은 관절을 움직인다.
//
//  [설계서와의 차이]
//      * 설계서는 ArmIK 하나다. 봉현님 분리를 따라 흡착 로봇은 BackArmIK를 쓴다 (11/03 팀 원칙).
//      * 흡착 전용 경로 함수(PlanSuctionApproach() · PreviewSuctionRotation() · StartPreparedSuction...())를 더 갖는다.
//      * FrontArmIK에 STEP08로 더한 Arrived · MoveThrough() · 도착 기준 보정 · Initialize()의 구동 값 맞추기는 없다.
//      * 설계서에 없는 칸: raisedElbowClearance, minimumWristPlane, driveStiffness · driveDamping · driveForceLimit, HighPrecision.
//  ============================================================
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class BackArmIK : MonoBehaviour
{
    //  ---------------- 연결 ----------------
    [Header("연결")]
    [Tooltip("흡착컵 끝 기준점 (Tool Center Point). BoxAttacher가 Configure()로 넘긴다")]
    public Transform tcp;

    //  ---------------- 자세 조건 ----------------
    [Header("자세 조건")]
    [Tooltip("팔꿈치를 목표 높이보다 이만큼 위에 둔 자세만 쓴다 m. 0이면 보지 않는다. BoxAttacher가 컨테이너 적재 중 0.10으로 바꾼다 (default: 0)")]
    [Min(0)] public float raisedElbowClearance;

    [Tooltip("손목(wrist1)부터 끝 링크가 내려가면 안 되는 높이 (월드 y, m). BoxAttacher가 집는 상자 밑면으로 바꾼다. 기본값은 사실상 끔 (default: -10000)")]
    public float minimumWristPlane = -10000f;

    //  ---------------- 관절 구동 ----------------
    [Header("관절 구동")]
    [Tooltip("관절 구동 강성. 이동 · Hold()가 이 값으로 맞춘다 (default: 100000)")]
    [Min(1)] public float driveStiffness = 100000f;

    [Tooltip("관절 구동 감쇠. BoxAttacher가 1500으로 바꾼다 (default: 10000)")]
    [Min(1)] public float driveDamping = 10000f;

    [Tooltip("관절 구동 힘 한계. BoxAttacher가 10000으로 바꾼다 (default: 1000)")]
    [Min(1)] public float driveForceLimit = 1000f;

    //  ---------------- BoxAttacher가 Configure()로 넘기는 값 ----------------
    BoxCollider targetBox;                                  //  보호할 상자 (팔이 닿으면 안 되는 상자). 이동 명령의 obstacle로도 바뀐다
    float degreesPerSecond = 20;                            //  관절 속도 °/s
    float metresPerSecond = .06f;                           //  손끝 속도 m/s
    float positionTolerance = .008f;                        //  도착 · 경로 허용 위치 오차 m
    float angleTolerance = 4;                               //  도착 허용 각도 오차 °
    float boxClearance = .008f;                             //  팔과 상자 사이 최소 간격 m
    bool compensateRobotGravity = true;                     //  로봇 링크 무게 상쇄


    //  ============================================================
    //  상태 (BoxAttacher · 에디터 도구가 읽는 값)
    //  ============================================================
    /// 경로를 따라 움직이는 중인지 (도착 · 정지 · 60초 초과 · 상자 보호로 끝나면 false)
    public bool Moving { get; private set; }

    /// 마지막 이동 목표, tcp와 목표 사이 거리 m
    public Vector3 GoalPosition { get; private set; }
    public Quaternion GoalRotation { get; private set; }
    public float PositionError => tcp == null ? 0 : Vector3.Distance(tcp.position, GoalPosition);

    /// 상태 문구가 바뀔 때 알림, 상태 문구
    public event Action<string> StatusChanged;
    public string Status { get; private set; }

    /// 켜면 정밀 배치: 경로 허용 오차 1.5 mm · 1.5°, 도착 기준 2 mm · 2° (PrecisePlacement와 같은 값)
    public bool HighPrecision { get => PrecisePlacement; set => PrecisePlacement = value; }


    //  ============================================================
    //  안쪽 값
    //  ============================================================
    //  internal: 같은 프로젝트의 다른 스크립트 · 에디터 도구가 직접 읽고 쓸 수 있게 열어 둔 값 (FrontArmIK와 같은 구조)
    internal ArmKinematics Model;                           //  관절 모델. 이동 명령마다 새로 만든다 (BoxAttacher.Start()도 직접 넣는다)
    internal readonly List<float[]> Route = new List<float[]>();    //  실행할 경로 (관절각 6개씩, 약 1° 간격)
    internal bool ProtectBox;                               //  켜면 이동 중 상자가 움직이거나 팔이 너무 가까워질 때 멈춘다
    internal string PathFailure;                            //  마지막 경로 실패 사유
    internal bool PrecisePlacement;                         //  정밀 배치 (HighPrecision). BoxAttacher가 켠다

    //  이 스크립트 안에서는 소문자 이름으로 쓴다 (위 칸과 같은 값. status는 바꿀 때 StatusChanged를 부른다)
    string status { get => Status; set { Status = value; StatusChanged?.Invoke(value); } }
    ArmKinematics model { get => Model; set => Model = value; }
    List<float[]> route => Route;
    bool protectBox { get => ProtectBox; set => ProtectBox = value; }
    string pathFailure { get => PathFailure; set => PathFailure = value; }
    bool precisePlacement => PrecisePlacement;

    //  실행 상태
    float[] solution;                                       //  지금 따라가는 경로 점 (route[routeIndex])
    float started;                                          //  이동 시작 시각 (60초 제한)
    int routeIndex;                                         //  지금 따라가는 경로 점 번호
    Vector3 plannedBoxPosition;                             //  경로를 시작할 때 상자 위치 (움직였는지 비교)
    Quaternion plannedBoxRotation;                          //  경로를 시작할 때 상자 회전
    ArticulationBody[] bodies;                              //  로봇의 모든 ArticulationBody (무게 상쇄용)
    readonly List<Behaviour> suspended = new List<Behaviour>();     //  Initialize()가 끈 URDF 조종 스크립트 (꺼질 때 다시 켠다)

    //  흡착 사전 경로 (PreviewSuctionRotation()이 저장하고, StartPreparedSuction...()이 그대로 실행한다)
    readonly List<float[]> preparedSuctionLift = new List<float[]>();       //  접촉 → 위로 들기
    readonly List<float[]> preparedSuctionTurn = new List<float[]>();       //  회전 대기 위치에서 나눠 돌리기
    readonly List<float[]> preparedSuctionTransfer = new List<float[]>();   //  든 위치 → 회전 대기 위치 (돌리지 않고 옮기기)
    float[] preparedTransferStart;                          //  각 구간을 시작할 때의 관절각 (실제 자세와 5° 넘게 다르면 실행하지 않는다)
    float[] preparedLiftStart, preparedTurnStart;


    //  ============================================================
    //  시작 · 끝
    //  ============================================================
    // BoxAttacher가 손끝 기준점 · 보호할 상자 · 속도 · 허용 오차 · 상자 간격 · 무게 상쇄를 넘긴다.
    /// [호출] BoxAttacher.Configure() (시작할 때, 이동 명령 전마다)
    public void Configure(Transform tool, BoxCollider obstacle, float angularSpeed, float linearSpeed, float positionError, float angleError, float clearance, bool gravity)
    {
        tcp = tool;
        targetBox = obstacle;
        degreesPerSecond = angularSpeed;
        metresPerSecond = linearSpeed;
        positionTolerance = positionError;
        angleTolerance = angleError;
        boxClearance = clearance;
        compensateRobotGravity = gravity;
    }

    // 준비 자세로 시작한다. URDF 조종 스크립트를 끄고, 관절마다 구동 목표를 준비 자세(없으면 지금 자세)로 둔다.
    /// [호출] BoxAttacher.Awake() (Configure() 다음)
    /// [!] 구동 강성 · 감쇠 · 힘 한계는 여기서 바꾸지 않는다 (첫 이동 · Hold() 때 맞춘다). 관절 모델(Model)도 만들어 두지 않는다.
    /// [!] tcp가 비어 있으면 관절은 건드리지 않는다 (조종 스크립트 끄기와 무게 상쇄 준비만 한다).
    public void Initialize(float[] initialJointAngles)
    {
        //  1) URDF Importer의 키보드 조종 스크립트도 xDrive를 쓰므로 끈다 (RestoreControllers()가 다시 켠다)
        foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null && component.enabled &&
                (component.GetType().FullName == "Unity.Robotics.UrdfImporter.Control.Controller" ||
                 component.GetType().FullName == "Unity.Robotics.UrdfImporter.Control.JointControl" ||
                 component.GetType().FullName == "JointControl"))
            {
                suspended.Add(component);
                component.enabled = false;
            }
        }
        bodies = GetComponentsInChildren<ArticulationBody>();

        //  2) 관절마다 구동 목표 = 준비 자세 (없으면 지금 자세)
        if (tcp != null)
        {
            var initialModel = new ArmKinematics(transform, tcp);
            bool hasSavedPose = initialJointAngles != null && initialJointAngles.Length == 6;
            for (int i = 0; i < 6; i++)
            {
                var joint = initialModel.Joints[i];

                //  Awake 중에는 관절 상태(jointPosition)를 쓰지 않고 구동 목표만 정한다.
                //  Unity 6에서 첫 물리 스텝 전에 여러 로봇의 관절 상태를 덮어쓰면 PhysX 관절 정보가 깨질 수 있다.
                //  준비 자세가 없으면 씬에 놓인 자세를 붙잡는다 (프리팹의 0° 목표로 팔이 접히지 않게).
                float currentDegrees = joint.jointPosition.dofCount > 0
                    ? joint.jointPosition[0] * Mathf.Rad2Deg
                    : joint.xDrive.target;
                var drive = joint.xDrive;
                drive.target = hasSavedPose ? initialJointAngles[i] : currentDegrees;
                joint.xDrive = drive;
            }
        }
    }

    // 꺼질 때 지금 관절각을 붙잡고, 끈 조종 스크립트를 다시 켠다.
    void OnDisable()
    {
        if (Application.isPlaying && model != null) { Hold(); }
        RestoreControllers();
    }

    // Initialize()가 끈 URDF 조종 스크립트를 다시 켠다.
    /// [호출] OnDisable()
    internal void RestoreControllers()
    {
        foreach (var component in suspended)
            if (component != null) { component.enabled = true; }
        suspended.Clear();
    }


    //  ============================================================
    //  이동 명령 (true = 이동 시작. 끝은 Moving · Status로 확인)
    //  ============================================================
    // 지금 자세에서 목표 위치 · 회전까지 손끝을 곧게 옮긴다.
    /// [호출] BoxAttacher.Move() · MoveToDropHeight() (운반 · 내려놓기 · 상승)
    /// [!] 움직이는 중이면 지금 이동을 멈추고 새로 시작한다. 상자 보호는 하지 않는다.
    public bool MoveTo(Vector3 position, Quaternion rotation)
    {
        if (!Application.isPlaying)
        {
            status = "Play 모드에서 실행하세요.";
            return false;
        }
        if (Moving) { StopMotion(); }

        try
        {
            model = new ArmKinematics(transform, tcp);
            var current = model.CurrentAngles();
            route.Clear();
            protectBox = false;
            if (!AppendCartesian(ref current, position, rotation, false))
            {
                route.Clear();
                status = "연속적인 직선 이동 경로를 찾지 못했습니다. " + pathFailure;
                return false;
            }
            BeginRoute(position, rotation);
            return true;
        }
        catch (Exception exception)
        {
            //  tcp가 비었거나 회전 관절이 6개가 아니면 ArmKinematics가 예외를 던진다
            //  [주의] 로그 앞머리가 "CorrectionSequence:"다 (FrontArmIK에서 옮겨 온 그대로). 실제로는 흡착 로봇 오류다
            status = exception.Message;
            Debug.LogError("CorrectionSequence: " + exception.Message, this);
            return false;
        }
    }

    // 목표 위치 · 회전의 관절각을 먼저 구하고, 지금 관절각에서 그 관절각까지 고르게 바꿔 간다 (손끝 경로는 곡선).
    /// [호출] BoxAttacher.LoadSeqRoutine() (윗면 흡착 접근, 예전 단순 경로)
    /// obstacle을 주면 그 상자와 겹치지 않는 자세 · 경로만 쓰고, 이동 중에도 상자를 보호한다.
    /// [!] targetBox가 obstacle로 바뀐다 (비우면 null). 움직이는 중이면 false.
    public bool MoveToJointPath(Vector3 position, Quaternion rotation, BoxCollider obstacle = null)
    {
        if (!Application.isPlaying || Moving) { return false; }

        model = new ArmKinematics(transform, tcp);
        var current = model.CurrentAngles();
        route.Clear();
        targetBox = obstacle;
        protectBox = obstacle != null;

        //  목표 자세 조건: 팔꿈치 높이, 손목 높이, (상자가 있으면) 상자와 간격
        if (!model.Solve(position, rotation, current, out var answer, out _, out _,
                precisePlacement ? 1.5f : 3f,
                candidate => (raisedElbowClearance <= 0 || model.HasRaisedElbow(candidate, position.y + raisedElbowClearance))
                             && model.WristAbovePlane(candidate, minimumWristPlane)
                             && (!protectBox || model.IsBoxClear(candidate, targetBox, boxClearance)),
                precisePlacement ? .0015f : positionTolerance)
            || !AppendJointEdge(current, answer, protectBox))
        {
            route.Clear();
            status = "관절 경로를 찾지 못했습니다. " + pathFailure;
            return false;
        }
        BeginRoute(position, rotation);
        return true;
    }

    // 정해 둔 관절각(준비 자세 등)까지 관절각을 고르게 바꿔 간다.
    /// [호출] BoxAttacher (집기 자세 · 접촉 자세 복귀, 다음 상자 전 준비 자세 복귀)
    /// obstacle을 주면 경로 내내 그 상자와 간격을 보고, 이동 중에도 상자를 보호한다.
    /// [!] targetBox가 obstacle로 바뀐다 (비우면 null). 움직이는 중이거나 관절각이 6개가 아니면 false.
    public bool MoveToJointAngles(float[] angles, BoxCollider obstacle = null)
    {
        if (!Application.isPlaying || Moving || angles == null || angles.Length != 6) { return false; }

        model = new ArmKinematics(transform, tcp);
        route.Clear();
        targetBox = obstacle;
        protectBox = obstacle != null;
        if (!AppendJointEdge(model.CurrentAngles(), angles, protectBox))
        {
            route.Clear();
            status = "복귀 경로 실패 · " + pathFailure;
            return false;
        }
        model.Forward(angles, out var goalPosition, out var goalRotation);
        BeginRoute(goalPosition, goalRotation);
        return true;
    }


    //  ============================================================
    //  흡착 경로 (옆면 흡착: 미리 계산 → 나눠 실행)
    //  ============================================================
    // 흡착 접근 경로를 만든다: 대기 위치(high) → 물러난 자리(standOff) → 접촉(contact). preview = false면 바로 이동을 시작한다.
    /// [호출] BoxAttacher.LoadToteRoutine() (방향 · 손목 회전 후보마다 preview로 계획)
    /// 접촉 자세를 먼저 풀고, 거기서 거꾸로 직선으로 물러나며 같은 팔꿈치 방향의 자세를 찾는다. 대기 위치까지는 관절 경로, 그 뒤는 그 역순이다.
    /// 모든 자세는 팔꿈치가 접촉 높이 + raisedElbowClearance 위, 손목이 minimumWristPlane 위여야 한다. 대기 위치까지는 상자와 간격도 본다.
    /// [!] preview = true면 Play 중이 아니어도 계획한다. 만든 경로는 route에 남는다 (PreviewSuctionRotation() · StartPreparedSuctionApproach()가 쓴다).
    public bool PlanSuctionApproach(Vector3 high, Vector3 standOff, Vector3 contact, Quaternion rotation, BoxCollider obstacle, bool preview = false)
    {
        if ((!Application.isPlaying && !preview) || Moving) { return false; }

        model = new ArmKinematics(transform, tcp);
        targetBox = obstacle;
        protectBox = true;
        route.Clear();
        var current = model.CurrentAngles();

        //  쓸 수 있는 자세: 팔꿈치 높이 · 손목 높이 조건
        bool acceptable(float[] candidate) => model.HasRaisedElbow(candidate, contact.y + raisedElbowClearance) && model.WristAbovePlane(candidate, minimumWristPlane);

        //  1) 접촉 자세를 먼저 푼다 (정밀: 1.5 mm · 1.5°)
        bool solved = model.Solve(contact, rotation, current, out var contactPose, out float error, out float angle, 1.5f, acceptable, .0015f);
        // Z-facing parcels can converge to the wrist-down branch even when a
        // wrist-up solution exists. Seed alternate wrist branches, but accept
        // exactly the same elbow, belt clearance and contact tolerances.
        if (!solved && obstacle != null && Mathf.Abs(obstacle.transform.forward.z) > .9f &&
            model.Solve(contact, rotation, current, out var freePose, out _, out _, 1.5f, null, .0015f))
        {
            foreach (int joint in new[] { 3, 4, 5, 0 })
            {
                foreach (float offset in new[] { -90f, 90f })
                {
                    var seedPose = (float[])freePose.Clone();
                    var body = model.Joints[joint];
                    var drive = body.xDrive;
                    float lower = body.twistLock == ArticulationDofLock.FreeMotion ? -360f : drive.lowerLimit;
                    float upper = body.twistLock == ArticulationDofLock.FreeMotion ? 360f : drive.upperLimit;
                    seedPose[joint] = Mathf.Clamp(seedPose[joint] + offset, lower, upper);
                    if (model.Solve(contact, rotation, seedPose, out var alternate, out _, out _, 1.5f, acceptable, .0015f))
                    { contactPose = alternate; solved = true; break; }
                }
                if (solved) break;
            }
        }
        if (!solved)
        {
            status = "흡착 접촉 자세 불가 · " + (error * 1000).ToString("F1") + "mm / " + angle.ToString("F1") + "도";
            return false;
        }

        //  2) 접촉 → 물러난 자리 → 대기 위치로 거꾸로 직선 경로를 만든다 (같은 팔꿈치 방향 유지)
        var seed = (float[])contactPose.Clone();
        route.Add((float[])seed.Clone());
        if (!AppendCartesian(ref seed, standOff, rotation, false) || !AppendCartesian(ref seed, high, rotation, true))
        {
            route.Clear();
            status = "흡착 직선 접근 경로 불가 · " + pathFailure;
            return false;
        }

        //  3) 실제 경로 = 지금 자세 → 대기 위치 (관절 경로, 상자 간격 확인) + 2)의 역순
        var reverse = route.Select(q => (float[])q.Clone()).Reverse().ToArray();
        route.Clear();
        if (!AppendJointEdge(current, seed, true))
        {
            route.Clear();
            status = "흡착 대기 위치 접근 불가 · " + pathFailure;
            return false;
        }
        foreach (var point in reverse)
        {
            AppendJointEdge(seed, point, false);            //  상자 간격을 보지 않으므로 실패하지 않는다
            seed = point;
        }
        protectBox = false;
        if (!preview) { BeginRoute(contact, rotation); }
        return true;
    }

    // 흡착한 뒤의 경로를 미리 계산해 저장한다: 들기 → (돌리지 않고) 회전 대기 위치로 옮기기 → 나눠 돌리기. 모두 되면 true.
    /// [호출] BoxAttacher.LoadToteRoutine() (PlanSuctionApproach() preview 바로 뒤, 회전 후보마다)
    /// 계산이 성공한 관절 경로를 그대로 저장해, 실행 때 다른 관절 해(IK branch)로 바뀌지 않게 한다.
    ///     liftHeight      접촉 자리에서 위로 드는 높이 m
    ///     rotationOffset  들어 올린 뒤 돌리기 전에 옮길 거리 (회전 대기 위치)
    ///     rotationRise    돌리면서 더 올리는 높이 m
    ///     stepDegrees     한 번에 돌리는 각도 (5 ~ 30°로 자른다)
    /// [!] route에 PlanSuctionApproach()가 만든 접근 경로가 있어야 한다. 끝나면 route를 그 접근 경로로 되돌린다.
    public bool PreviewSuctionRotation(BoxCollider box, Quaternion desiredBox, float liftHeight, float rotationRise, float stepDegrees, Vector3 rotationOffset = default)
    {
        preparedSuctionLift.Clear();
        preparedSuctionTurn.Clear();
        preparedSuctionTransfer.Clear();
        preparedTransferStart = null;
        preparedLiftStart = preparedTurnStart = null;
        if (Moving || model == null || route.Count == 0 || box == null) { return false; }

        var approach = route.Select(q => (float[])q.Clone()).ToArray();
        try
        {
            //  1) 들기: 접촉 자세에서 liftHeight만큼 위로
            var seed = (float[])approach[approach.Length - 1].Clone();
            var liftStart = (float[])seed.Clone();
            model.Forward(seed, out var contact, out var contactRotation);
            route.Clear();
            if (!AppendSuctionTransit(ref seed, contact + Vector3.up * liftHeight, contactRotation, true))
            {
                status = "흡착 후 상승 경로 불가 · " + pathFailure;
                return false;
            }
            var lift = route.Select(q => (float[])q.Clone()).ToArray();
            var transferStart = (float[])seed.Clone();
            route.Clear();

            //  2) 옮기기: 든 상자를 돌리지 않고 회전 대기 위치로 (돌리기 경로를 고르기 전에)
            if (!AppendSuctionTransit(ref seed, contact + Vector3.up * liftHeight + rotationOffset, contactRotation, true))
            {
                status = "회전 대기 위치 이동 경로 불가 · " + pathFailure;
                return false;
            }
            var transfer = route.Select(q => (float[])q.Clone()).ToArray();
            var turnStart = (float[])seed.Clone();
            route.Clear();

            //  3) 돌리기: 상자 가운데를 기준으로 원하는 회전까지 stepDegrees씩 나눠 돌린다 (돌리면서 rotationRise만큼 더 올린다)
            Vector3 centre = box.bounds.center + Vector3.up * liftHeight + rotationOffset;
            Vector3 offset = contact - box.bounds.center;
            Quaternion delta = desiredBox * Quaternion.Inverse(box.transform.rotation);
            int count = Mathf.Max(1, Mathf.CeilToInt(Quaternion.Angle(Quaternion.identity, delta) / Mathf.Clamp(stepDegrees, 5f, 30f)));
            for (int n = 1; n <= count; n++)
            {
                float t = (float)n / count;
                var turn = Quaternion.Slerp(Quaternion.identity, delta, t);
                if (!AppendSuctionTransit(ref seed, centre + Vector3.up * rotationRise * t + turn * offset, turn * contactRotation, n == count))
                {
                    status = "흡착 후 회전 경로 불가 (" + n + "/" + count + ") · " + pathFailure;
                    return false;
                }
            }

            //  4) 세 구간과 각 시작 자세를 저장한다
            preparedSuctionTransfer.AddRange(transfer);
            preparedTransferStart = transferStart;
            preparedSuctionLift.AddRange(lift);
            preparedSuctionTurn.AddRange(route.Select(q => (float[])q.Clone()));
            preparedLiftStart = liftStart;
            preparedTurnStart = turnStart;
            return true;
        }
        finally
        {
            //  성공 · 실패 모두 route를 접근 경로로 되돌린다
            route.Clear();
            route.AddRange(approach);
        }
    }

    // PlanSuctionApproach()가 route에 남긴 접근 경로로 이동을 시작한다.
    /// [호출] BoxAttacher.LoadToteRoutine() (후보를 고른 뒤)
    /// [!] 경로를 새로 계산하지 않는다. route가 비어 있거나 움직이는 중이면 false.
    public bool StartPreparedSuctionApproach(Vector3 contact, Quaternion rotation)
    {
        if (!Application.isPlaying || Moving || route.Count == 0) { return false; }

        protectBox = false;
        BeginRoute(contact, rotation);
        return true;
    }

    // 저장한 들기 · 옮기기 · 돌리기 구간을 실행한다 (흡착한 뒤 차례로).
    /// [호출] BoxAttacher.LoadToteRoutine()
    public bool StartPreparedSuctionLift() => StartPreparedSuctionSegment(preparedSuctionLift, preparedLiftStart);
    public bool StartPreparedSuctionTransfer() => StartPreparedSuctionSegment(preparedSuctionTransfer, preparedTransferStart);
    public bool StartPreparedSuctionTurn() => StartPreparedSuctionSegment(preparedSuctionTurn, preparedTurnStart);

    // 저장한 구간 하나를 실행한다. 지금 관절각이 그 구간의 시작 자세와 관절마다 5° 안일 때만 시작한다.
    /// [!] 상자 보호는 하지 않는다. 목표 = 구간 마지막 자세의 손끝.
    bool StartPreparedSuctionSegment(List<float[]> segment, float[] expectedStart)
    {
        if (!Application.isPlaying || Moving || segment.Count == 0 || expectedStart == null)
        {
            status = "사전 확인한 흡착 이동 경로가 없습니다.";
            return false;
        }

        model = new ArmKinematics(transform, tcp);
        var actual = model.CurrentAngles();
        for (int j = 0; j < actual.Length; j++)
        {
            if (Mathf.Abs(actual[j] - expectedStart[j]) > 5f)
            {
                status = "사전 경로 시작 자세에서 관절이 벗어나 중단했습니다.";
                return false;
            }
        }
        route.Clear();
        route.AddRange(segment.Select(q => (float[])q.Clone()));
        protectBox = false;
        model.Forward(route[route.Count - 1], out var position, out var rotation);
        BeginRoute(position, rotation);
        return true;
    }


    //  ============================================================
    //  정지
    //  ============================================================
    // 이동을 끝내고 지금 관절각을 구동 목표로 붙잡는다 (경로는 지운다). 상태 문구는 바꾸지 않는다.
    /// [호출] BoxAttacher.Start(), StopMotion(), OnDisable()
    /// [!] 관절 모델이 없으면 붙잡지 않는다 (Initialize()는 모델을 만들지 않는다. BoxAttacher.Start()가 넣는다).
    public void Hold()
    {
        Moving = false;
        route.Clear();
        if (model == null) { return; }

        var angles = model.CurrentAngles();
        for (int i = 0; i < angles.Length; i++)
        {
            var drive = model.Joints[i].xDrive;
            drive.target = angles[i];
            drive.targetVelocity = 0;
            drive.stiffness = driveStiffness;
            drive.damping = driveDamping;
            drive.forceLimit = driveForceLimit;
            model.Joints[i].xDrive = drive;
        }
    }

    // Hold() 후 상태 문구를 "정지"로 바꾼다.
    /// [호출] BoxAttacher (중단 · 놓는 높이 도착), MoveTo(), FixedUpdate() (상자 보호 · 60초 초과)
    public void StopMotion()
    {
        Hold();
        status = "정지 · 현재 관절 각도를 유지합니다.";
    }


    //  ============================================================
    //  실행 (물리 스텝마다)
    //  ============================================================
    // 로봇 무게를 상쇄하고, 움직이는 중이면 구동 목표를 다음 경로 점 쪽으로 속도 제한만큼 옮긴다.
    /// 상자 보호, 다음 점으로 넘어가기, 도착 판정, 60초 제한도 여기서 한다.
    void FixedUpdate()
    {
        //  1) 로봇 링크 무게 상쇄 (Initialize() 뒤 항상. 상자에는 중력이 그대로 걸린다)
        if (compensateRobotGravity && bodies != null)
        {
            foreach (var body in bodies)
            {
                if (body != null && body.useGravity && !body.immovable) { body.AddForce(-Physics.gravity * body.mass); }
            }
        }
        if (!Moving || model == null) { return; }

        //  2) 상자 보호: 상자가 1 cm · 6° 넘게 움직였거나, 팔과 상자 간격이 boxClearance의 40%보다 좁으면 멈춘다
        if (protectBox && (targetBox == null || Vector3.Distance(targetBox.transform.position, plannedBoxPosition) > .01f || Quaternion.Angle(targetBox.transform.rotation, plannedBoxRotation) > 6))
        {
            StopMotion();
            status = "박스가 움직여 경로를 중단했습니다. 다시 계획하세요.";
            return;
        }
        if (protectBox && !model.IsBoxClear(model.CurrentAngles(), targetBox, boxClearance * .4f))
        {
            StopMotion();
            status = "박스 접근 여유가 부족하여 정지했습니다.";
            return;
        }

        //  3) 지금 구동 목표(commanded)와 다음 경로 점(solution)의 차이, 실제 관절이 구동 목표에서 뒤처진 정도
        float largestStep = 0;
        float trackingError = 0;
        var actual = model.CurrentAngles();
        var commanded = new float[6];
        for (int i = 0; i < 6; i++)
        {
            commanded[i] = model.Joints[i].xDrive.target;
            largestStep = Mathf.Max(largestStep, Mathf.Abs(solution[i] - commanded[i]));
            trackingError = Mathf.Max(trackingError, Mathf.Abs(actual[i] - commanded[i]));
        }

        //  4) 이번 스텝에 갈 비율: 관절 속도 · 손끝 속도 제한. 관절이 3° 넘게 뒤처지면 기다린다 (0)
        model.Forward(commanded, out var commandPosition, out _);
        model.Forward(solution, out var nextPosition, out _);
        float fraction = 1;
        if (largestStep > .0001f) { fraction = Mathf.Min(fraction, degreesPerSecond * Time.fixedDeltaTime / largestStep); }
        float distance = Vector3.Distance(commandPosition, nextPosition);
        if (distance > .00001f) { fraction = Mathf.Min(fraction, metresPerSecond * Time.fixedDeltaTime / distance); }
        if (trackingError > 3) { fraction = 0; }

        //  5) 구동 목표를 그 비율만큼 옮긴다
        for (int i = 0; i < model.Joints.Length; i++)
        {
            var joint = model.Joints[i];
            var drive = joint.xDrive;
            drive.stiffness = driveStiffness;
            drive.damping = driveDamping;
            drive.forceLimit = driveForceLimit;
            drive.targetVelocity = 0;
            drive.target = Mathf.Lerp(commanded[i], solution[i], fraction);
            joint.xDrive = drive;
        }

        //  6) 구동 목표가 이 점에 닿았고 관절도 따라왔으면 다음 점으로
        if (fraction >= .999f && trackingError < 2.8f && routeIndex < route.Count - 1)
        {
            routeIndex++;
            solution = route[routeIndex];
        }

        //  7) 도착 판정: 마지막 점이고 손끝이 허용 오차 안 (정밀 배치 2 mm · 2°)
        //      [!] 문구 앞부분 "목표 도착"을 BoxAttacher.Arrived가 본다. 문구를 바꾸면 도착 판정이 깨진다 ("손가락" 부분은 FrontArmIK에서 옮겨 온 그대로)
        if (routeIndex == route.Count - 1 && PositionError < (precisePlacement ? .002f : positionTolerance) && Quaternion.Angle(tcp.rotation, GoalRotation) < (precisePlacement ? 2f : angleTolerance))
        {
            Moving = false;
            status = "목표 도착 · 손가락을 닫거나 다음 동작을 선택하세요.";
        }
        else if (Time.time - started > 60)
        {
            //  8) 60초 안에 도착하지 못하면 멈춘다
            StopMotion();
            status = "이동 중단 · 60초 내 도착하지 못함. 충돌/관절 설정을 확인하세요.";
        }
    }


    //  ============================================================
    //  경로 만들기
    //  ============================================================
    // 흡착한 상자를 들고 옮기는 직선 구간 (PreviewSuctionRotation() 전용). 안 되면 구간을 반으로 나눠 다시 해 본다 (최대 3번 나눔).
    /// 마지막 자세(finalPose)는 1.5 mm, 중간 자세는 3 mm 오차까지 허용한다 (빈 공간에서 돌릴 때는 조금 덜 정확해도 된다). 회전 걸음은 2°.
    /// [!] 실패하면 이번에 붙인 경로를 모두 지우고 seed를 처음 자세로 되돌린다.
    bool AppendSuctionTransit(ref float[] seed, Vector3 endPosition, Quaternion endRotation, bool finalPose, int depth = 0)
    {
        int routeStart = route.Count;
        var original = (float[])seed.Clone();
        float tolerance = finalPose ? .0015f : .003f;

        //  1) 한 번에 곧게
        if (AppendCartesian(ref seed, endPosition, endRotation, false, tolerance, 2f)) { return true; }
        route.RemoveRange(routeStart, route.Count - routeStart);
        seed = (float[])original.Clone();
        if (depth >= 3) { return false; }

        //  2) 가운데 점까지 → 끝까지, 두 번에 나눠서
        model.Forward(seed, out var startPosition, out var startRotation);
        if (AppendSuctionTransit(ref seed, Vector3.Lerp(startPosition, endPosition, .5f),
                Quaternion.Slerp(startRotation, endRotation, .5f), false, depth + 1)
            && AppendSuctionTransit(ref seed, endPosition, endRotation, finalPose, depth + 1)) { return true; }

        //  실패한 후보의 반쪽 경로가 실행 경로에 남지 않게 지운다
        route.RemoveRange(routeStart, route.Count - routeStart);
        seed = original;
        return false;
    }

    // seed 자세의 손끝에서 목표까지 직선을 짧은 걸음(1.2 cm 또는 rotationStep°)으로 나누고, 걸음마다 관절각을 풀어 route에 붙인다. seed는 끝 자세로 바뀐다.
    /// [호출] MoveTo(), PlanSuctionApproach(), AppendSuctionTransit()
    /// 걸음마다 앞 자세에서 관절이 28° 넘게 꺾이지 않고, 팔꿈치 · 손목 높이 조건을 지키는 자세만 쓴다.
    /// 허용 위치 오차: transitTolerance > 0이면 마지막 걸음은 그 값, 중간 걸음은 3 mm. 아니면 정밀 1.5 mm, 보통 positionTolerance.
    /// [!] 실패하면 false. 그때까지 붙인 경로는 지우지 않는다 (부르는 쪽이 지운다).
    internal bool AppendCartesian(ref float[] seed, Vector3 endPosition, Quaternion endRotation, bool checkBox, float transitTolerance = -1f, float rotationStep = 4f)
    {
        model.Forward(seed, out var startPosition, out var startRotation);
        int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Vector3.Distance(startPosition, endPosition) / .012f, Quaternion.Angle(startRotation, endRotation) / rotationStep)));
        for (int step = 1; step <= steps; step++)
        {
            float progress = (float)step / steps;
            var previous = seed;

            //  이 걸음에서 쓸 수 있는 자세: 앞 자세에서 크게 꺾이지 않고, 팔꿈치 · 손목 높이 조건을 지킨다
            bool continuous(float[] candidate)
            {
                for (int j = 0; j < candidate.Length; j++)
                    if (Mathf.Abs(candidate[j] - previous[j]) > 28f) { return false; }
                return (raisedElbowClearance <= 0 || model.HasRaisedElbow(candidate, Mathf.Lerp(startPosition.y, endPosition.y, progress) + raisedElbowClearance)) && model.WristAbovePlane(candidate, minimumWristPlane);
            }

            if (!model.Solve(Vector3.Lerp(startPosition, endPosition, progress), Quaternion.Slerp(startRotation, endRotation, progress), seed,
                    out var next, out float error, out float angle,
                    // Z held-transit samples use the same 2-degree orientation
                    // tolerance as drive arrival. Contact and final placement
                    // endpoints retain 1.5 degrees and their original position limits.
                    precisePlacement ? (transitTolerance > 0 && targetBox != null && Mathf.Abs(targetBox.transform.forward.z) > .9f &&
                        (step < steps || transitTolerance > .0015f) ? 2f : 1.5f) : 5f,
                    continuous,
                    transitTolerance > 0 ? (step == steps ? transitTolerance : .003f) : (precisePlacement ? .0015f : positionTolerance)))
            {
                pathFailure = $"중간 자세 IK 오차 {error * 1000:F1}mm / {angle:F1}도";
                return false;
            }

            //  풀린 자세가 앞 자세에서 크게 꺾이면 직선 구간으로 쓰지 않는다
            float largest = 0;
            for (int j = 0; j < 6; j++)
                { largest = Mathf.Max(largest, Mathf.Abs(next[j] - seed[j])); }
            if (largest > 28)
            {
                pathFailure = "중간 자세에서 관절이 크게 꺾입니다.";
                return false;
            }
            if (!AppendJointEdge(seed, next, checkBox)) { return false; }
            seed = next;
        }
        return true;
    }

    // from → to 관절각을 약 1°씩 나눠 route에 붙인다 (from 자체는 넣지 않는다). checkBox면 점마다 상자와 간격을 본다.
    /// [호출] 이동 명령들, PlanSuctionApproach(), AppendCartesian()
    internal bool AppendJointEdge(float[] from, float[] to, bool checkBox)
    {
        //  가장 많이 도는 관절 기준 1°마다 한 점
        float largest = 0;
        for (int j = 0; j < 6; j++)
            { largest = Mathf.Max(largest, Mathf.Abs(to[j] - from[j])); }
        int samples = Mathf.Max(1, Mathf.CeilToInt(largest));
        for (int sample = 1; sample <= samples; sample++)
        {
            var point = new float[6];
            for (int j = 0; j < 6; j++)
                { point[j] = Mathf.Lerp(from[j], to[j], (float)sample / samples); }
            if (checkBox && !model.IsBoxClear(point, targetBox, boxClearance))
            {
                pathFailure = "중간 경로가 박스의 여유 공간을 침범합니다.";
                return false;
            }
            route.Add(point);
        }
        return true;
    }

    // 만든 route로 이동을 시작한다. 목표 · 시작 시각 · 상자 자리를 기록하고 Moving = true.
    /// [호출] 이동 명령들, 흡착 경로 함수들
    /// [!] route가 비어 있으면 안 된다 (route[0]을 읽는다).
    internal void BeginRoute(Vector3 position, Quaternion rotation)
    {
        GoalPosition = position;
        GoalRotation = rotation;
        routeIndex = 0;
        solution = route[0];
        Moving = true;
        started = Time.time;

        //  상자 보호용: 지금 상자 자리
        if (targetBox != null)
        {
            plannedBoxPosition = targetBox.transform.position;
            plannedBoxRotation = targetBox.transform.rotation;
        }
        status = "상승·회전·접근 경로 이동 중";
    }
}
