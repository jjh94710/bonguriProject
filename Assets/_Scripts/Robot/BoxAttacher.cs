using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

//  ============================================================
//  BoxAttacher
//  흡착 로봇(fr5_suction)의 흡착 적재. 상자 하나를 흡착해 컨테이너(ToteBase)의 다음 칸에 넣는 순서 전체를 맡는다.
//      - 흡착: 흡착컵이 상자 면에 닿으면 상자를 kinematic으로 바꾸고 tcp를 따라오게 한다 (Attach(), FixedUpdate()). 놓을 때 되돌린다 (Detach()).
//      - 적재: 5 × 6 면 흡착 → 들어 올리기 → 5 × 6 면이 위로 오게 돌리기 → 컨테이너 칸 위로 운반 → 내려가며 놓기 → 칸 등록 (LoadToteRoutine()).
//      - 로봇 이동은 모두 BackArmIK가 한다. 이 스크립트는 목표 위치 · 자세, 순서, 성공 · 실패 판단을 맡는다.
//
//  [적재 경로] 상자의 5 × 6 면(로컬 ±Z)이 월드 어느 축을 보는지로 경로가 나뉜다 (자동 판별).
//      Z       손목 위쪽 해를 탐색하고 사전 검증된 접근 · 상승 · 이동 · 분할 회전 경로로 적재한다
//      X · Y   사전 확인 경로: 접근 · 들기 · 회전 대기 위치 이동 · 나눠 돌리기 경로를 모두 미리 계산해 본 뒤 시작한다
//              (X Direction · Y Direction 칸의 값을 쓴다)
//  [사용] fr5_suction 루트에 붙인다 (BackArmIK는 자동으로 같이 붙는다). 칸은 봉현님 도구 Tools/Bonguri/Configure Suction Loading이 맞춘다.
//         LoadNextBox()로 시작하고, Busy가 false가 되면 LastLoadSucceeded로 결과를 본다.
//         공정에서는 CellController가 레인 끝 상자를 TryLoadBox()로 넘긴다 (지훈님 통합).
//  [!] 컨테이너 칸 이름을 toto → tote로 바꿨다. 씬에 저장된 값은 그대로 읽고(FormerlySerializedAs), 예전 이름 toto도 코드에서 쓸 수 있다.
//  [!] kinematic 상자는 흡착하지 않는다. 벨트 위 상자(kinematic)는 물리를 켠 뒤 LoadNextBox()를 부른다.
//  [!] Tote 칸이 비어 있으면 예전 단순 적재 경로(LoadSeqRoutine(): 윗면 흡착 → Slots 칸)로 돈다. 공정에서는 Tote를 꼭 연결한다.
//  [!] StopLoading()은 그 자리에서 멈춘다. 들고 있던 상자는 흡착한 채로 남는다.
//  [!] 흡착은 1번만 시도한다. 실패하면 Status에 이유를 남기고 끝낸다 (LastLoadSucceeded = false).
//
//  [설계서와의 차이]
//      * 설계서: 붙이기 · 떼기만 하고 적재 순서는 LoadSequence가 정한다. 현재 구현은 적재 순서 전체를 갖는다 (11/03 팀 원칙).
//        LoadSequence는 targetBox 지정 → LoadNextBox() → 끝나면 결과 확인만 한다.
//      * Attach()는 상자만 받는다 (설계서: Attach(BoxInfo)). 흡착 면 · 접촉 위치를 확인하고, 맞지 않으면 붙이지 않는다.
//      * 흡착하기 전에 상자 아래의 컨베이어(RoboLacyTech LinearConveyor)를 멈춘다 (FactoryExample 씬용. 공정 벨트 ConveyorLine과는 관계없다).
//  ============================================================
[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(BackArmIK))]
[DisallowMultipleComponent]
public sealed class BoxAttacher : MonoBehaviour
{
    //  ---------------- 연결 ----------------    (FR-08)
    [Header("연결")]
    [Tooltip("같은 로봇의 BackArmIK. 비우면 같은 오브젝트에서 찾는다")]
    public BackArmIK armIK;

    [Tooltip("넣을 컨테이너 (ToteBase). 공정에서는 꼭 연결한다. 비우면 예전 단순 적재 경로로 돈다")]
    [FormerlySerializedAs("toto")]
    public ToteBase tote;

    [Tooltip("흡착컵 끝 기준점 (Tool Center Point). 상자 면에 닿는 점")]
    public Transform tcp;

    [Tooltip("흡착컵 오브젝트. 컵 → tcp 방향이 흡착하는 방향이다")]
    public Transform suctionCup;

    [Tooltip("컨테이너 오브젝트. 예전 단순 적재 경로에서 테두리 높이를 잴 때만 쓴다 (ToteBase.StackNextBox()가 채운다)")]
    public Transform container;

    [Tooltip("이번에 집을 상자 (LoadSequence가 지정). 비어 있거나 이미 넣은 상자면 LoadNextBox()가 집기 위치 0.3 m 안의 상자를 찾는다")]
    public BoxCollider targetBox;

    [Tooltip("적재 칸 (컨테이너 칸과 같게 8칸). 칸 수가 최대 적재 수이다 (ToteBase.StackNextBox()가 채운다)")]
    public Transform[] slots = new Transform[6];

    [Tooltip("준비 자세 관절각 6개 °. 시작할 때 이 자세로 간다. 비우면 씬에 놓인 자세를 붙잡는다")]
    public float[] readyAngles;

    //  ---------------- 방향별 경로 ----------------
    // 5 × 6 면이 보는 월드 축별 접근 · 상승 · 회전 경로 값.
    [Serializable]
    public sealed class DirectionSettings
    {
        [Tooltip("흡착 면 바깥쪽으로 떨어져 기다리는 거리 m. 여기서 면에 곧게 다가간다 (default: 0.16)")]
        [Min(.03f)] public float standOff = .16f;

        [Tooltip("기다리는 자리보다 이만큼 위에서 내려와 접근한다 m (default: 0.12)")]
        [Min(0)] public float approachAbove = .12f;

        [Tooltip("흡착 뒤 들어 올리는 높이 m (default: 0.16)")]
        [Min(.05f)] public float liftHeight = .16f;

        [Tooltip("나눠 돌리는 동안 함께 올리는 높이 m (default: 0.025)")]
        [Range(0, .08f)] public float rotationRise = .025f;

        [Tooltip("나눠 돌리는 한 번의 각도 °. 실제로는 10° 이하로 쓴다 (default: 15)")]
        [Range(5, 30)] public float rotationStepDegrees = 15f;

        [Tooltip("흡착 면 둘레로 손목을 돌려 볼 각도 후보 ° (앞에서부터 시도)")]
        public float[] wristRolls = { 0f, 90f, -90f, 180f };
    }

    [Header("방향별 경로 (5 × 6 면이 보는 월드 축, 자동 판별)")]
    [Tooltip("5 × 6 면이 월드 X 쪽을 볼 때. X는 손목 각도 45° 간격 후보와 들기 높이 0.12 · 0.08 m도 더 시도한다")]
    public DirectionSettings xDirection = new DirectionSettings { 
        standOff = .16f, approachAbove = .12f, liftHeight = .16f, rotationRise = .025f, wristRolls = new[] { 90f, -90f, 0f, 180f } };

    [Tooltip("5 × 6 면이 위 · 아래를 볼 때")]
    public DirectionSettings yDirection = new DirectionSettings { 
        standOff = .12f, approachAbove = .08f, liftHeight = .12f, rotationRise = .02f };

    [Tooltip("Z 방향도 접근·상승·이동·분할 회전 경로를 미리 검증한다")]
    public DirectionSettings zDirection = new DirectionSettings {
        standOff = .16f, approachAbove = .12f, liftHeight = .16f, rotationRise = .025f };

    [Tooltip("마지막 상자의 5 × 6 면 방향과 고른 경로 (확인용)")]
    [SerializeField] string activeFaceDirection = "자동 판별 · 접근·상승·회전 사전 확인";

    //  ---------------- 이동 ----------------
    [Header("이동")]
    [Tooltip("운반 높이 여유 m. 컨테이너 테두리 · 칸 위로 이만큼 띄워 옮긴다. Z 방향 경로의 접근 거리 · 들기 높이로도 쓴다 (default: 0.16)")]
    [Min(.03f)] public float clearance = .16f;

    [Tooltip("흡착할 때 tcp와 흡착 지점 사이 허용 거리 m (default: 0.008)")]
    [Min(.001f)] public float contactTolerance = .008f;

    //  ---------------- 상태 ----------------
    [Header("상태")]
    [Tooltip("지금 상태 문구 (진행 단계 · 실패 이유). 확인용")]
    public string Status = "Play 후 다음 박스 적재를 누르세요.";


    //  ============================================================
    //  상태 (LoadSequence · ToteBase · 에디터 도구가 읽는 값)
    //  ============================================================
    /// 적재 중인지, 마지막 적재가 끝까지 성공했는지 (칸 등록 + 팔 복귀)
    public bool Busy => routine != null;
    public bool LastLoadSucceeded { get; private set; }

    /// 준비가 끝났는지 (Start() 끝에서 true, 꺼지면 false). CellController가 상자를 넘기기 전에 본다
    public bool IsReady { get; private set; }

    /// 예전 이름 toto로 tote를 읽고 쓴다 (CellController · BonguriIntegrationSetup 등 예전 이름을 쓰는 코드용)
    /// [!] 저장되는 칸은 tote 하나다. 씬에 저장된 예전 값(toto)은 FormerlySerializedAs로 tote가 읽는다.
    public ToteBase toto { get => tote; set => tote = value; }

    /// 넣은 상자 수 (지워진 상자는 빼고 센다), 넣은 상자 목록
    public int LoadedCount => placed.Count(b => b != null);
    public IReadOnlyList<BoxCollider> PlacedBoxes => placed;

    /// 집기 위치 (시작할 때 targetBox 가운데. 없으면 tcp 위치)
    public Vector3 PickupPosition => pickupCentre;

    /// 상자를 흡착하고 있는지
    public bool Attached => heldBody != null;


    //  ============================================================
    //  안쪽 값
    //  ============================================================
    bool placementReached;                                  //  MoveToDropHeight()가 놓는 높이에 닿았는지
    readonly List<BoxCollider> placed = new List<BoxCollider>();                                    //  넣은 상자
    readonly List<(Collider robot, Collider parcel)> ignored = new List<(Collider, Collider)>();    //  흡착 중 충돌을 끈 로봇 · 상자 Collider 쌍 (RestoreContacts()가 되돌린다)
    Coroutine routine;                                      //  적재 코루틴 (Busy)

    //  흡착한 상자
    BoxCollider held;
    Rigidbody heldBody;
    bool heldWasKinematic;                                  //  흡착 전 isKinematic (Detach()가 되돌린다)
    bool heldUsedGravity;                                   //  흡착 전 useGravity
    Vector3 heldLocalPosition;                              //  tcp 기준 상자 위치 (FixedUpdate()가 따라가게 한다)
    Quaternion heldLocalRotation;                           //  tcp 기준 상자 회전

    //  자세 기억
    Vector3 pickupCentre;                                   //  집기 위치
    float[] pickupAngles;                                   //  (예전 경로) 상자 위 접근 자세 관절각
    float[] contactAngles;                                  //  (예전 경로) 윗면 접촉 자세 관절각
    float[] cycleReadyAngles;                               //  상자마다 돌아가는 준비 자세 관절각
    Quaternion cycleToolRotation;                           //  준비 자세의 tcp 회전 (로봇 기준)
    Vector3 contactPoint;                                   //  (예전 경로) contactAngles를 기억한 접촉 위치
    bool cancelled;                                         //  StopLoading()이 켠다
    float suctionFloor = -10000f;                           //  손목이 내려가면 안 되는 높이 = 집는 상자 밑면 + 1 mm (Configure()가 BackArmIK에 넘긴다)
    readonly List<(Collider a, Collider b)> selfPairs = new List<(Collider, Collider)>();                    //  로봇 자기 Collider끼리 끈 충돌 쌍
    readonly List<(ArticulationBody body, bool gravity)> gravityStates = new List<(ArticulationBody, bool)>();   //  관절 중력 원래 값 (꺼질 때 되돌린다)

    //  흡착 면 확인 (로컬 Z 축이 5 × 6 면의 법선이다)
    Vector3 requiredSuctionPoint;                           //  흡착해야 할 지점 (ToteBase.SuctionPoint())
    Vector3 requiredSuctionNormal;                          //  그 면의 바깥 법선
    bool requiredSuctionFace;                               //  켜면 Attach()가 이 면 · 지점으로 확인한다 (컨테이너 적재 중)


    //  ============================================================
    //  시작 · 끝
    //  ============================================================
    // 로봇 자기 Collider끼리 충돌을 끄고, 관절 중력을 끄고 물리 반복 횟수를 올린다. tcp가 있으면 준비 자세로 시작한다.
    /// 관절 구동 목표는 BackArmIK.Initialize()가 정한다. 여기서 관절 상태 배열을 직접 바꾸면 Unity 6 PhysX가 멈출 수 있다.
    void Awake()
    {
        if (armIK == null) { armIK = GetComponent<BackArmIK>(); }

        //  1) 로봇 자기 Collider끼리 충돌 끄기 (꺼질 때 되돌린다)
        var colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
            for (int j = i + 1; j < colliders.Length; j++)
                if (!Physics.GetIgnoreCollision(colliders[i], colliders[j]))
                {
                    Physics.IgnoreCollision(colliders[i], colliders[j], true);
                    selfPairs.Add((colliders[i], colliders[j]));
                }

        //  2) 관절: 중력 끄기 (원래 값 기억), 물리 반복 횟수 · 최대 관절 속도
        foreach (var body in GetComponentsInChildren<ArticulationBody>())
        {
            gravityStates.Add((body, body.useGravity));
            body.useGravity = false;
            body.solverIterations = 60;
            body.solverVelocityIterations = 20;
            body.maxJointVelocity = 4f;
        }

        //  3) 준비 자세
        if (tcp != null)
        {
            Configure();
            armIK.Initialize(readyAngles);
        }
    }

    // 연결을 확인하고, 집기 위치 · 준비 자세(관절각 · tcp 회전)를 기억한다.
    /// 물리 한 프레임을 기다린 뒤 지금 관절각을 붙잡는다 (Hold()).
    IEnumerator Start()
    {
        if (armIK == null) { armIK = GetComponent<BackArmIK>(); }
        if (tcp == null || suctionCup == null || slots == null || slots.Length == 0 || slots.Any(s => s == null))
            { Status = "TCP / 흡착컵 / 적재 슬롯을 연결하세요."; yield break; }
        pickupCentre = targetBox == null ? tcp.position : targetBox.bounds.center;
        Configure();
        yield return new WaitForFixedUpdate();
        armIK.Model = new ArmKinematics(transform, tcp);
        armIK.Hold();
        cycleReadyAngles = armIK.Model.CurrentAngles();
        cycleToolRotation = Quaternion.Inverse(transform.rotation) * tcp.rotation;
        IsReady = true;
        Status = "흡착 적재 준비 완료 · 다음 박스 적재";
    }

    // 꺼질 때 관절 중력을 되돌리고, 적재를 멈추고, 상자를 놓고, 끈 충돌을 모두 되돌린다.
    void OnDisable()
    {
        IsReady = false;
        foreach (var entry in gravityStates)
        {
            if (entry.body != null) { entry.body.useGravity = entry.gravity; }
        }
        gravityStates.Clear();
        StopAllCoroutines();
        routine = null;
        Detach();
        RestoreContacts();
        foreach (var pair in selfPairs)
            if (pair.a != null && pair.b != null) { Physics.IgnoreCollision(pair.a, pair.b, false); }
        selfPairs.Clear();
    }

    // BackArmIK에 이동 값을 넘긴다. 이동 명령 전마다 부른다.
    /// 컨테이너 적재 중에는 손목 높이 제한(집는 상자 밑면) · 팔꿈치 10 cm 위 자세만 쓴다.
    /// 속도 20 °/s · 0.07 m/s, 허용 오차 3 mm · 3°, 상자 간격 6 mm, 로봇 무게 상쇄, 정밀 배치.
    void Configure()
    {
        armIK.minimumWristPlane = tote != null ? suctionFloor : -10000f;
        armIK.raisedElbowClearance = tote != null && !(Attached && activeFaceDirection.StartsWith("Z")) ? .10f : 0;
        armIK.driveDamping = 1500f;
        armIK.driveForceLimit = 10000f;
        armIK.Configure(tcp, null, 20, .07f, .003f, 3, .006f, true);
        armIK.PrecisePlacement = true;
    }


    //  ============================================================
    //  흡착 (붙이기 · 떼기)
    //  ============================================================
    // 상자를 흡착한다. 흡착컵이 정해진 면 · 지점에 닿아 있어야 붙인다.
    /// [호출] LoadSeqRoutine() · LoadToteRoutine() (접근이 끝난 뒤)
    /// 컨테이너 적재 중: 5 × 6 면 흡착 지점에 수직으로 닿아야 한다 (AttachRequiredFace()). 예전 경로: 윗면에 닿고 컵이 아래를 봐야 한다.
    /// 붙이면 로봇 · 상자 충돌을 끄고, 상자를 kinematic으로 바꿔 tcp를 따라오게 한다 (FixedUpdate()).
    /// [!] Play 중이 아니거나, 이미 흡착 중이거나, Rigidbody가 없거나 kinematic인 상자는 false.
    public bool Attach(BoxCollider box)
    {
        if (!Application.isPlaying || Attached || box == null || box.attachedRigidbody == null || box.attachedRigidbody.isKinematic) 
            { return false; }

        //  1) 접촉 확인
        if(requiredSuctionFace)
        {
            //  컨테이너 적재 중: 5 x 6 면 확인. 실패 문구는 AttachRequiredFace()가 쓴다
            if (!AttachRequiredFace(box)) { return false; }
        }
        else
        {
            //  예전 경로: 윗면에 닿고 컵이 아래를 봐야 한다
            Vector3 top = box.bounds.center;
            top.y = box.bounds.max.y;
            Vector3 axis = (tcp.position - suctionCup.position).normalized;
            if(Vector3.Distance(tcp.position, top) > contactTolerance || Vector3.Dot(axis,Vector3.down) < .98f)
                { Status = "흡착 실패 · 컵이 박스 윗면에 닿고 아래를 향해야 합니다."; return false; }
        }
        held = box;

        //  2) 로봇 · 상자 충돌 끄기 (RestoreContacts()가 되돌린다)
        foreach (var a in GetComponentsInChildren<Collider>())
            foreach (var b in box.attachedRigidbody.GetComponentsInChildren<Collider>())
                if (!Physics.GetIgnoreCollision(a, b))
                {
                    Physics.IgnoreCollision(a, b, true);
                    ignored.Add((a, b));
                }

        //  3) 상자 물리 값을 기억하고, tcp 기준 위치 · 회전을 정한 뒤 kinematic으로 바꾼다
        var body = box.attachedRigidbody;
        body.solverIterations = 20;
        body.solverVelocityIterations = 10;
        heldBody = body;
        heldWasKinematic = body.isKinematic;
        heldUsedGravity = body.useGravity;
        heldLocalPosition = tcp.InverseTransformPoint(body.position);
        heldLocalRotation = Quaternion.Inverse(tcp.rotation) * body.rotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
        Status = "흡착 고정됨";
        return true;
    }

    // 컨테이너 적재의 흡착 접촉 확인. 받는 크기 상자이고, tcp가 흡착 지점에서 contactTolerance 안이고, 컵이 면에 수직(약 11° 안)이면 true.
    bool AttachRequiredFace(BoxCollider box)
    {
        Vector3 axis = (tcp.position - suctionCup.position).normalized;
        if (!tote.IsCorrectSize(box) || Vector3.Distance(tcp.position, requiredSuctionPoint) > contactTolerance || Vector3.Dot(axis, -requiredSuctionNormal) < .98f)
            { Status = "흡착 중단 · 컵이 5×6cm 면 중심에 수직으로 접촉하지 않았습니다."; return false; }
        return true;
    }

    // 흡착을 놓는다. 상자의 kinematic · 중력을 흡착 전 값으로 되돌리고, 물리 상자면 속도를 0으로 둔다.
    /// [!] 로봇 · 상자 충돌은 여기서 되돌리지 않는다. 팔이 물러난 뒤 RestoreContacts()로 되돌린다.
    public void Detach()
    {
        if (heldBody != null)
        {
            var body = heldBody;
            heldBody = null;
            body.isKinematic = heldWasKinematic;
            body.useGravity = heldUsedGravity;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
        held = null;
    }

    // 흡착한 상자를 tcp를 따라 옮긴다 (물리 프레임마다).
    void FixedUpdate()
    {
        if (heldBody == null || tcp == null) { return; }
        heldBody.MovePosition(tcp.TransformPoint(heldLocalPosition));
        heldBody.MoveRotation(tcp.rotation * heldLocalRotation);
    }

    // Attach()가 끈 로봇 · 상자 충돌을 되돌린다.
    void RestoreContacts()
    {
        foreach (var pair in ignored)
            if (pair.robot != null && pair.parcel != null) { Physics.IgnoreCollision(pair.robot, pair.parcel, false); }
        ignored.Clear();
    }


    //  ============================================================
    //  적재 명령
    //  ============================================================
    // 공정 인계: 넘겨받은 상자 하나만 집기 시작한다 (주변 상자를 찾지 않는다). 시작하면 true.
    /// [호출] CellController (레인 끝에 닿은 상자를 넘길 때)
    /// 준비 · 동작 중 · 상자 · 수거함 연결 · 다음 칸을 차례로 확인하고, 문제가 있으면 error에 이유를 넣고 false.
    /// [!] 시작하지 못하면 상자를 다시 kinematic으로 둔다 (컨베이어 정지 상태 유지).
    public bool TryLoadBox(BoxCollider box, out string error)
    {
        error = null;
        if (!Application.isPlaying || !isActiveAndEnabled || !IsReady)
            { error = "적재 로봇 초기화 대기"; }
        else if (Busy || Attached || armIK == null || armIK.Moving)
            { error = "적재 로봇 동작 중"; }
        else if (box == null || box.attachedRigidbody == null)
            { error = "적재 상자의 Collider/Rigidbody가 없습니다."; }
        else if (placed.Contains(box))
            { error = "이미 적재한 상자입니다."; }
        else if (tote == null || tote.loader != this || container != tote.transform)
            { error = "수거함 양방향 연결을 확인하세요."; }
        else if (slots == null || slots.Length != tote.Capacity || slots.Any(s => s == null) || !slots.SequenceEqual(tote.slots))
            { error = "수거함 슬롯 연결을 확인하세요."; }
        else if (!tote.TryNext(box, out _, out _, out string reason))
            { error = reason; }
        if (error != null) { return false; }

        //  넘겨받은 상자의 물리를 켜고 적재를 시작한다
        targetBox = box;
        var body = box.attachedRigidbody;
        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        LoadNextBox();
        if (Busy) { return true; }

        //  접수되지 않은 상자는 컨베이어 정지 상태로 유지한다
        body.isKinematic = true;
        error = Status;
        return false;
    }

    // 다음 상자 하나를 넣기 시작한다. 시작하면 Busy가 true가 되고, 끝나면 false가 된다.
    /// [호출] TryLoadBox(), LoadSequence, ToteBase.StackNextBox()
    /// targetBox가 비어 있거나 이미 넣은 상자면, 집기 위치 0.3 m 안에서 가장 가까운 물리 상자(자식 "CardboardBody" 있음)를 고른다.
    /// [!] Play 중이 아니거나, 적재 중이거나, 팔이 움직이는 중이면 아무것도 하지 않는다. 칸 · 상자 문제면 Status만 바꾸고 시작하지 않는다.
    public void LoadNextBox()
    {
        if (!Application.isPlaying || Busy || armIK == null || armIK.Moving) { return; }
        if (slots == null || slots.Length == 0 || slots.Any(s => s == null)) 
            { Status = "적재 슬롯을 연결하세요."; return; }
        if (LoadedCount >= slots.Length) 
            { Status = "수거함 가득 참 · " + LoadedCount + "/" + slots.Length; return; }
        if (targetBox == null || placed.Contains(targetBox))
        {
            targetBox = FindObjectsByType<BoxCollider>().Where(b => 
                            b.attachedRigidbody != null && !b.attachedRigidbody.isKinematic && !placed.Contains(b) && b.transform.Find("CardboardBody") != null && 
                            Vector3.Distance(b.bounds.center, pickupCentre) < .3f).OrderBy(b => Vector3.Distance(b.bounds.center, pickupCentre)).FirstOrDefault();
        }
        if (targetBox == null) 
            { Status = "벨트 집기 위치에 다음 박스가 없습니다."; return; }
        LastLoadSucceeded = false;
        cancelled = false;
        routine = StartCoroutine(LoadSeqRoutine());
    }

    // 적재를 멈춘다. 팔은 지금 관절각을 붙잡고, 들고 있던 상자는 흡착한 채로 남는다.
    /// [호출] LoadSequence (제한 시간 초과), ToteBase.StopAutoStack()
    public void StopLoading()
    {
        cancelled = true;
        if (routine != null) { StopCoroutine(routine); }
        routine = null;
        if (armIK != null) { armIK.StopMotion(); }
        Status = Attached ? "중단 · 흡착 유지 중" : "중단";
    }


    //  ============================================================
    //  이동 도우미
    //  ============================================================
    /// 마지막 이동이 목표에 도착했는지 (멈춤 명령이 없고, BackArmIK 상태 문구가 "목표 도착"으로 시작)
    bool Arrived => !cancelled && armIK.Status != null && armIK.Status.StartsWith("목표 도착");

    // tcp를 point · rotation으로 곧게 옮기고 끝날 때까지 기다린다. 결과는 Arrived로 본다.
    IEnumerator Move(Vector3 point, Quaternion rotation)
    {
        Configure();
        if (!armIK.MoveTo(point, rotation))
            { Status = "이동 실패 [" + Status + "] · " + armIK.Status + " goal=" + point + " target=" + targetBox.bounds.center; yield break; }
        while (armIK.Moving && !cancelled) { yield return null; }
        if (!Arrived) { Status = "이동 중단 · " + armIK.Status; }
    }

    // 놓는 높이(point)로 내려가면서, 흡착을 놓아도 되는지(ToteBase.ReadyToRelease()) 물리 프레임마다 본다.
    /// 0.15초 이어서 놓아도 되면 멈추고 placementReached = true.
    /// 이동이 끝난 뒤에도 0.4초 동안 다시 확인한다 (마지막 물리 프레임의 실제 높이). 끝내 안 되면 Status에 이유를 쓴다.
    IEnumerator MoveToDropHeight(BoxCollider box, int index, Vector3 centre, Vector3 point, Quaternion rotation)
    {
        placementReached = false;
        Configure();
        bool planned = armIK.MoveTo(point, rotation);
        float supportedSince = -1f;

        //  1) 내려가는 동안 확인
        do
        {
            if (cancelled || !Attached) { yield break; }
            if (tote.ReadyToRelease(index, box, centre))
            {
                if (supportedSince < 0) { supportedSince = Time.time; }
                if (Time.time - supportedSince >= .15f)
                    { armIK.StopMotion(); placementReached = true; Status = "입구 위 해제 높이 도착 · 흡착 해제"; yield break; }
            }
            else { supportedSince = -1f; }
            if (!planned || !armIK.Moving) { break; }
            yield return new WaitForFixedUpdate();
        } while (true);

        //  2) 이동이 끝난 뒤 0.4초 동안 다시 확인
        float deadline = Time.time + .4f;
        while (!cancelled && Attached && Time.time < deadline)
        {
            if (tote.ReadyToRelease(index, box, centre))
                { armIK.StopMotion(); placementReached = true; yield break; }
            yield return new WaitForFixedUpdate();
        }
        Status = "놓기 중단 · 칸 정렬/입구 위 높이 확인 실패 · " + armIK.Status;
    }


    //  ============================================================
    //  적재 순서
    //  ============================================================
    // 적재 코루틴. 컨테이너(Tote)가 연결되어 있으면 LoadToteRoutine()으로 넘긴다.
    /// 아래는 Tote가 없을 때의 예전 단순 적재 경로: 윗면 흡착 → 들기 → 빈 슬롯 위로 운반 → 내려놓기 → 확인.
    IEnumerator LoadSeqRoutine()
    {
        yield return null;
        if (tote != null) 
            { yield return LoadToteRoutine(); yield break; }
        var box = targetBox;

        //  1) 상자 아래 컨베이어를 멈추고, 빈 슬롯을 고른다
        foreach (var hit in Physics.RaycastAll(box.bounds.center + Vector3.up * .1f, Vector3.down, .5f))
        {
            var conveyor = hit.collider.GetComponentInParent<RoboLacyTech.Conveyors.LinearConveyor>();
            if (conveyor != null) { conveyor.Stop(); }
        }
        int index = Array.FindIndex(slots, s => s != null && !placed.Any(b => b != null && Vector3.Distance(b.bounds.center, s.position) < .035f));
        if (index < 0 || SlotOccupied(index, box)) 
            { Status = "빈 슬롯이 없거나 놓는 자리에 장애물이 있습니다."; routine = null; yield break; }

        //  2) 상자 위로 접근 (기억한 자세가 있으면 그 자세로, 없으면 손목 각도 0 · 90 · -90 · 180°를 차례로 시도)
        Vector3 top = box.bounds.center;
        top.y = box.bounds.max.y + .001f;
        Vector3 localAxis = tcp.InverseTransformDirection((tcp.position - suctionCup.position).normalized);
        Quaternion down = Quaternion.FromToRotation(tcp.rotation * localAxis, Vector3.down) * tcp.rotation;
        Status = "박스 위 접근";
        Configure();
        bool approachFound = pickupAngles != null && armIK.MoveToJointAngles(pickupAngles, box);
        foreach (float yaw in approachFound ? Array.Empty<float>() : new[] { 0f, 90f, -90f, 180f })
        {
            var candidate = Quaternion.AngleAxis(yaw, Vector3.up) * down;
            if (armIK.MoveToJointPath(top + Vector3.up * clearance, candidate, box)) 
                { down = candidate; approachFound = true; break; }
        }
        if (!approachFound) 
            { Status = "접근 실패 · " + armIK.Status; routine = null; yield break; }
        while (armIK.Moving && !cancelled) { yield return null; }
        if (!Arrived) { routine = null; yield break; }
        pickupAngles = new ArmKinematics(transform, tcp).CurrentAngles();
        down = tcp.rotation;

        //  3) 윗면 접촉 (3 cm 안의 같은 자리면 기억한 접촉 자세로) → 흡착
        Status = "흡착면 접촉";
        if (contactAngles != null && Vector3.Distance(contactPoint, top) < .03f)
        {
            if (!armIK.MoveToJointAngles(contactAngles)) 
                { Status = "접촉 복귀 실패 · " + armIK.Status; routine = null; yield break; }
            while (armIK.Moving && !cancelled) { yield return null; }
        }
        else
        {
            yield return Move(top, down);
            if (Arrived)
            {
                contactAngles = new ArmKinematics(transform, tcp).CurrentAngles();
                contactPoint = top;
            }
        }
        if (!Arrived || !Attach(box)) 
            { routine = null; yield break; }
        yield return new WaitForSeconds(.3f);

        //  4) 들어 올리기 (5 cm 이상 올라가야 한다)
        float bottom = box.bounds.min.y;
        Status = "박스 들어 올리기";
        yield return Move(tcp.position + Vector3.up * clearance, down);
        if (!Arrived || !Attached || box.bounds.min.y - bottom < .05f) 
            { Status = "들어 올리기 실패 · 흡착 상태를 확인하세요."; routine = null; yield break; }

        //  5) 수거함 위로 운반 (위로 → 슬롯 위로) → 내려놓기
        Vector3 offset = tcp.position - box.bounds.center;
        Vector3 place = slots[index].position + offset;
        float rim = container.GetComponentsInChildren<Collider>().Max(c => c.bounds.max.y);
        float carryY = Mathf.Max(tcp.position.y, place.y + clearance, rim + clearance + offset.y);
        Status = "수거함 위로 운반";
        yield return Move(new Vector3(tcp.position.x, carryY, tcp.position.z), down);
        if (!Arrived) 
            { routine = null; yield break; }
        yield return Move(new Vector3(place.x, carryY, place.z), down);
        if (!Arrived) 
            { routine = null; yield break; }
        Status = "슬롯 " + (index + 1) + " 내려놓기";
        yield return Move(place + Vector3.up * .002f, down);
        if (!Arrived || !Attached) 
            { routine = null; yield break; }

        //  6) 흡착 해제 → 위로 복귀 → 슬롯 위치(1.8 cm 안) · 멈춤 확인 → 등록
        Detach();
        yield return new WaitForSeconds(.4f);
        Status = "흡착 해제 후 상승";
        yield return Move(new Vector3(place.x, carryY, place.z), down);
        RestoreContacts();
        yield return new WaitForSeconds(.5f);
        if (!Arrived || Vector3.Distance(box.bounds.center, slots[index].position) > .018f || box.attachedRigidbody.linearVelocity.magnitude > .05f)
            { Status = "적재 확인 실패 · 슬롯 위치와 박스 자세를 확인하세요."; routine = null; yield break; }
        placed.Add(box);
        Status = "적재 완료 · " + LoadedCount + "/" + slots.Length + " (슬롯 " + (index + 1) + ")";
        routine = null;
    }

    // (예전 경로) index 슬롯 자리에 다른 상자가 있는지 (상자 크기의 80%로 겹침 검사, 트리거 · 로봇 제외).
    bool SlotOccupied(int index, BoxCollider own)
    {
        Vector3 centre = slots[index].position;
        return Physics.OverlapBox(centre, own.bounds.extents * .8f, Quaternion.identity).Any(c => 
                    c != own && !c.isTrigger && c.attachedRigidbody != null && !c.transform.IsChildOf(transform));
    }

    // 컨테이너 적재 코루틴 (공정에서 쓰는 경로). 상자 하나를 5 × 6 면으로 흡착해 ToteBase의 다음 칸에 넣는다.
    /// [순서] 준비 자세 → 다음 칸 확인 → 5 × 6 면 방향 판별 → 흡착 면 · 손목 각도 고르기(경로 미리 계산) → 접근 · 흡착
    ///        → 들기 → 5 × 6 면을 위로 돌리기 → 칸 다시 확인 → 컨테이너 위로 운반 → 내려가며 놓기 → 복귀 → 칸 등록
    /// [결과] 칸에 등록되면 placed에 더한다. LastLoadSucceeded는 팔이 운반 높이로 돌아왔을 때만 true.
    ///        실패하면 그 자리에서 끝내고 Status에 이유를 쓴다 (흡착 중이면 상자를 든 채 멈춘다).
    IEnumerator LoadToteRoutine()
    {
        yield return null;
        var box = targetBox;
        suctionFloor = box.bounds.min.y + .001f;

        //  1) 준비 자세: 처음이면 지금 자세를 기억하고, 두 번째 상자부터는 같은 준비 자세로 돌아간 뒤 시작한다
        if (cycleReadyAngles == null)
        {
            cycleReadyAngles = new ArmKinematics(transform, tcp).CurrentAngles();
            cycleToolRotation = Quaternion.Inverse(transform.rotation) * tcp.rotation;
        }
        if (LoadedCount > 0)
        {
            Status = "다음 박스 전 동일 준비 자세로 복귀";
            Configure();
            if (!armIK.MoveToJointAngles(cycleReadyAngles, box))
                { Status = "준비 자세 복귀 실패 · " + armIK.Status; routine = null; yield break; }
            while (armIK.Moving && !cancelled) { yield return null; }
            if (!Arrived) 
                { Status = "준비 자세 복귀 중단 · " + armIK.Status; routine = null; yield break; }
        }
        Quaternion referenceToolRotation = transform.rotation * cycleToolRotation;

        //  2) 넣을 칸 확인 → 상자 아래 컨베이어 멈춤
        if (!tote.TryNext(box, out int index, out Vector3 slotCentre, out string reason))
            { Status = reason; routine = null; yield break; }
        foreach (var hit in Physics.RaycastAll(box.bounds.center + Vector3.up * .1f, Vector3.down, .5f))
        {
            var belt = hit.collider.GetComponentInParent<RoboLacyTech.Conveyors.LinearConveyor>();
            if (belt != null) { belt.Stop(); }
        }

        //  3) 5 × 6 면(로컬 Z)이 보는 월드 축을 판별하고 경로 값을 고른다 (X · Y · Z 방향별 사전 검증 경로)
        Vector3 cupAxis = tcp.InverseTransformDirection((tcp.position - suctionCup.position).normalized);
        Vector3 faceAxis = box.transform.forward.normalized;
        Vector3 absolute = new Vector3(Mathf.Abs(faceAxis.x), Mathf.Abs(faceAxis.y), Mathf.Abs(faceAxis.z));
        string direction = absolute.y >= absolute.x && absolute.y >= absolute.z ? "Y" : absolute.x > absolute.z ? "X" : "Z";
        // A single Cartesian turn can leave the safe wrist branch found for Z contact.
        DirectionSettings settings = direction == "X" ? xDirection : direction == "Y" ? yDirection : zDirection;
        if (settings == null)
            { Status = direction + " 방향 설정이 없습니다."; routine = null; yield break; }
        activeFaceDirection = direction + " 방향 · " + "접근·상승·회전 사전 확인";
        float approachDistance = Mathf.Max(.03f, settings.standOff);
        float approachAbove = Mathf.Max(0f, settings.approachAbove);
        float liftHeight = Mathf.Max(.05f, settings.liftHeight);
        float[] rollCandidates = settings.wristRolls;
        if (rollCandidates == null || rollCandidates.Length == 0) 
            { rollCandidates = new[] { 0f, 90f, -90f, 180f }; }
        if (direction == "X")
            { rollCandidates = new[] { 0f, 90f, -90f, 180f, 45f, -45f, 135f, -135f }.Concat(rollCandidates).Distinct().ToArray(); }
        else if (direction == "Z")
            { rollCandidates = new[] { 180f, 0f, 90f, -90f, 45f, -45f, 135f, -135f }.Concat(rollCandidates).Distinct().ToArray(); }
        string rejectedPath = "", rejectedApproach = "", rejectedRotation = "";
        int validApproaches = 0, rotationAttempts = 0;
        Quaternion selectedBoxRotation = Quaternion.identity;

        //  4) 흡착할 면(±Z)과 손목 각도 고르기
        //     두 면 중 위를 보고 로봇 쪽을 보는 면부터 시도한다. 벨트에 닿은 면(아래를 봄)은 건너뛴다
        float score(float sign)
        {
            Vector3 normal = box.transform.forward * sign;
            return Vector3.Dot(normal, Vector3.up) * 2 + Vector3.Dot(normal, (transform.position - box.bounds.center).normalized);
        }
        bool found = false;
        foreach (float sign in new[] { 1f, -1f }.OrderByDescending(score))
        {
            var normal = box.transform.forward.normalized * sign;
            if (Vector3.Dot(normal, Vector3.up) < -.5f) { continue; }
            var point = tote.SuctionPoint(box, sign);
            var aligned = Quaternion.FromToRotation(referenceToolRotation * cupAxis, -normal) * referenceToolRotation;
            foreach (float roll in rollCandidates)
            {
                //  4-1) 접근 경로: 면 위쪽 → 면 바깥 대기 자리 → 접촉 (모든 방향은 계산만 해 둔다)
                var rotation = Quaternion.AngleAxis(roll, normal) * aligned;
                Status = direction + " 방향 · 접근/회전 경로 계산";
                Configure();
                Vector3 contact = point + normal * .001f;
                if (!armIK.PlanSuctionApproach(point + 
                                               normal * approachDistance + 
                                               Vector3.up * approachAbove, point + 
                                               normal * approachDistance, contact, rotation, box, preview: true))
                    { rejectedPath = rejectedApproach = armIK.Status; continue; }
                validApproaches++;

                //  4-2) X · Y · Z: 흡착 뒤 들기 → 회전 대기 위치 → 나눠 돌리기 경로를 미리 계산해, 되는 조합이 있을 때만 이 접근을 쓴다
                {
                    bool rotationFound = false;
                    Quaternion canonical = tote.PlacementRotation(sign);
                    //  X: 180° 돌린 자세도 같은 5 × 6 바닥이라 후보에 넣는다 (손목을 덜 돌릴 수 있다)
                    var desiredRotations = direction == "Z" ? new[] { Quaternion.AngleAxis(-90f, tote.transform.up) * canonical, canonical, Quaternion.AngleAxis(180f, tote.transform.up) * canonical, Quaternion.AngleAxis(90f, tote.transform.up) * canonical } : direction != "Y" ? new[] { canonical, Quaternion.AngleAxis(180f, tote.transform.up) * canonical } : new[] { canonical };
                    var liftOptions = direction == "Z" ? new[] { .08f, .12f, liftHeight }.Distinct().ToArray() : direction != "Y" ? new[] { liftHeight, .12f, .08f }.Distinct().ToArray() : new[] { liftHeight };
                    //  회전 대기 위치 후보: 들어 올린 자리 기준 오프셋 (월드 m). 로봇 쪽 · 옆으로 옮겨 다른 손목 자세를 얻는다
                    Vector3 inward = Vector3.ProjectOnPlane(transform.position - box.bounds.center, Vector3.up).normalized;
                    Vector3 lateral = Vector3.Cross(Vector3.up, inward);
                    var waypoints = new[] {
                        inward * .08f + Vector3.up * .04f,
                        inward * .08f + lateral * .10f + Vector3.up * .04f,
                        inward * .08f - lateral * .10f + Vector3.up * .04f,
                        lateral * .12f + Vector3.up * .08f,
                        -lateral * .12f + Vector3.up * .08f,
                        Vector3.up * .10f
                    };
                    // Start with the verified wrist-up Z corridor. Every candidate
                    // still passes the same IK and complete-route checks below.
                    if (direction == "Z")
                        waypoints = new[] { waypoints[4], waypoints[2], waypoints[0], waypoints[1], waypoints[3], waypoints[5] };
                    var riseOptions = direction != "Y" ?
                        new[] { settings.rotationRise, 0f }.Distinct().ToArray() : new[] { settings.rotationRise };
                    foreach (var desired in desiredRotations)
                    {
                        foreach (float lift in liftOptions)
                        {
                            foreach (float rise in riseOptions)
                            {
                                foreach (var waypoint in waypoints)
                                {
                                    rotationAttempts++;
                                    Status = direction + " 방향 · 회전 대기 위치 후보 " + rotationAttempts + " 계산";
                                    if (armIK.PreviewSuctionRotation(box, desired, lift, rise, Mathf.Min(settings.rotationStepDegrees, 10f), waypoint))
                                        { selectedBoxRotation = desired; rotationFound = true; Debug.Log($"[Pickup Path] {name} | {direction} sign={sign} roll={roll} lift={lift:F3} rise={rise:F3} offset={waypoint.ToString("F3")} attempts={rotationAttempts}", this); break; }
                                    rejectedPath = rejectedRotation = armIK.Status;
                                    yield return null;
                                    if (cancelled) 
                                        { routine = null; yield break; }
                                }
                                if (rotationFound) { break; }
                            }
                            if (rotationFound) { break; }
                        }
                        if (rotationFound) { break; }
                    }
                    if (!rotationFound) 
                        { yield return null; continue; }
                    if (!armIK.StartPreparedSuctionApproach(contact, rotation))
                        { rejectedPath = "검증한 접근 경로 시작 실패"; continue; }
                }

                //  4-3) 검증한 면 · 손목 각도로 정한다
                requiredSuctionPoint = point;
                requiredSuctionNormal = normal;
                found = true;
                break;
            }
            if (found) { break; }
        }
        if (!found)
        {
            Status = direction + " 방향 경로 선택 실패 · 접근 성공 " + validApproaches + "개 / 회전 확인 " + rotationAttempts + "회 · "
                   + (validApproaches > 0 ? rejectedRotation : rejectedApproach);
            routine = null;
            yield break;
        }

        //  5) 접근 → 흡착 (5 × 6 면 흡착 지점에 수직으로 닿았는지 확인)
        Status = "5×6cm 면 옆/위 대기 위치로 이동";
        while (armIK.Moving && !cancelled) { yield return null; }
        if (!Arrived) 
            { Status = "흡착면 접근 중단 · " + armIK.Status; routine = null; yield break; }
        requiredSuctionFace = true;
        bool attached = Attach(box);
        requiredSuctionFace = false;
        if (!attached) 
            { routine = null; yield break; }
        yield return new WaitForSeconds(.3f);

        //  6) 들어 올리기 (4 cm 이상 올라가야 한다)
        float bottom = box.bounds.min.y;
        Status = "5×6cm 면 흡착 후 들어 올리기";
        {
            if (!armIK.StartPreparedSuctionLift())
                { Status = direction + " 방향 사전 상승 경로 시작 실패 · " + armIK.Status; routine = null; yield break; }
            while (armIK.Moving && !cancelled) { yield return null; }
        }
        if (!Arrived || !Attached || box.bounds.min.y - bottom < .04f)
            { Status = "흡착 상승 실패 · " + armIK.Status; routine = null; yield break; }

        //  7) 5 × 6 면이 위로 오게 돌리기 (목표 자세와 6° 안이어야 한다)
        Quaternion desiredBox = selectedBoxRotation;
        {
            //  X · Y · Z: 미리 계산한 경로로 회전 대기 위치까지 옮긴 뒤 나눠 돌린다
            Status = direction + " 방향 · 흡착 유지 후 회전 대기 위치로 이동";
            if (!armIK.StartPreparedSuctionTransfer())
                { Status = direction + " 방향 중간 위치 이동 시작 실패 · " + armIK.Status; routine = null; yield break; }
            while (armIK.Moving && !cancelled) { yield return null; }
            if (!Arrived || !Attached || cancelled)
                { Status = direction + " 방향 중간 위치 이동 중단 · " + armIK.Status; routine = null; yield break; }
            Status = direction + " 방향 · 중간 위치에서 사전 확인한 분할 회전";
            if (!armIK.StartPreparedSuctionTurn())
                { Status = direction + " 방향 사전 회전 경로 시작 실패 · " + armIK.Status; routine = null; yield break; }
            while (armIK.Moving && !cancelled) { yield return null; }
        }
        if (!Arrived || !Attached || Quaternion.Angle(box.transform.rotation, desiredBox) > 6)
            { Status = direction + " 방향 적재 자세 회전 실패 · " + armIK.Status; routine = null; yield break; }

        //  8) 같은 칸이 아직 비어 있고 받칠 수 있는지 다시 확인 (이제 모든 상자가 5 × 6 바닥, 로컬 Z가 7 cm 높이)
        if (!tote.TryNext(box, out int checkedIndex, out slotCentre, out reason) || checkedIndex != index)
            { Status = "적재 지지 재확인 실패 · " + reason; routine = null; yield break; }

        //  9) 컨테이너 위로 운반 (운반 높이로 올린 뒤 칸 위로)
        Vector3 offset = tcp.position - box.bounds.center;
        Vector3 place = slotCentre + offset;
        float rim = tote.RimHeight;
        Vector3 releaseCentre = tote.DropCentre(box, slotCentre);
        Vector3 releasePoint = releaseCentre + offset;
        float carryY = Mathf.Max(tcp.position.y, rim + clearance + offset.y, place.y + clearance);
        Quaternion toolRotation = tcp.rotation;
        if (direction == "Z")
        {
            Status = "Z 방향 · 들어 올린 높이에서 로봇 쪽으로 운반";
            Vector3 inwardTravel = Vector3.ProjectOnPlane(transform.position - tcp.position, Vector3.up).normalized * .12f;
            yield return Move(tcp.position + inwardTravel, toolRotation);
            if (!Arrived) { routine = null; yield break; }
        }
        Status = "수거함 위로 운반";
        yield return Move(new Vector3(tcp.position.x, carryY, tcp.position.z), toolRotation);
        if (!Arrived) { routine = null; yield break; }
        yield return Move(new Vector3(place.x, carryY, place.z), toolRotation);
        if (!Arrived) { routine = null; yield break; }

        //  10) 내려가며 놓기: 테두리 위 놓는 높이에서 흡착을 놓으면 상자가 떨어져 칸에 들어간다
        Status = (index < 4 ? "아래층 " : "위층 ") + (index % 4 + 1) + "번 입구 위에서 놓기";
        yield return MoveToDropHeight(box, index, slotCentre, releasePoint, toolRotation);
        if (!placementReached || !Attached || cancelled) { routine = null; yield break; }
        Detach();
        yield return new WaitForSeconds(.25f);

        //  11) 운반 높이로 복귀 → 충돌 되돌리기 → 상자가 칸에 멈출 때까지 최대 5초 → 칸 등록
        Status = "흡착 해제 후 상승";
        yield return Move(new Vector3(tcp.position.x, carryY, tcp.position.z), toolRotation);
        bool retracted = Arrived;
        RestoreContacts();
        float deadline = Time.time + 5f;
        while (Time.time < deadline && !cancelled && !tote.IsSettledAt(index, box)) 
            { yield return new WaitForFixedUpdate(); }
        if (cancelled || !tote.Register(index, box))
            { Status = "적재 확인 실패 · " + tote.Status; routine = null; yield break; }
        placed.Add(box);
        LastLoadSucceeded = retracted;
        Status = retracted ? tote.Status : "박스 적재됨 · 팔 복귀 실패로 반복 중단 · " + armIK.Status;
        routine = null;
    }
}
