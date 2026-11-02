using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using RoboLacyTech.Conveyors;

//  ============================================================
//  BoxPusher
//  분기 판(푸셔). 판을 물리로 밀어 벨트 위 상자 1개를 옆 벨트로 넘기거나(ConveyorTransfer) 수거함으로 떨어뜨린다(RejectToBin).
//      - 판은 kinematic Rigidbody이고, MovePosition으로 움직여 상자를 실제 충돌로 민다.
//      - 동작 단계(state): 0 대기 · 1 밀기 · 2 도착 후 잠깐 정지 · 3 복귀 · 4 정지(재설정 필요) · 5 수거함 낙하 확인
//      - 시작: BeginPush()(밀기) · BeginReject()(불량 배출, RejectToBin 모드만) · ReportInspection()(검사 결과로 배출)
//
//  [사용] 판 오브젝트에 붙인다. 밀 상자(parcel), 도착 벨트(receiver) 또는 수거함, 도착 · 수거 확인 영역(월드 좌표)을 Inspector에서 넣는다.
//
//  [설계서와의 차이]
//      * 설계서는 상자가 분기 지점에 오면 ConveyorLine이 TryPush(line, box)를 부르고, box.destination이 담당 목적지(target)면 민다.
//        여기는 담당 목적지 · TryPush가 없고, 밀 상자를 parcel 칸 하나로 받는다 (상자마다 누가 넣어 줘야 한다).
//      * 벨트가 RoboLacyTech LinearConveyor(실물 마찰 벨트)다. 공정 벨트 ConveyorLine은 상자를 kinematic으로 스크립트 이동하므로,
//        판이 상자를 밀려면 ConveyorLine.Take(box) → 상자 물리 켜기가 먼저 필요하다 (kinematic끼리는 충돌로 밀리지 않는다).
//      * 밀어낸 뒤 toLine.Feed(box) · toTote.Add(box)로 넘기는 연결과 PushedCount가 없다.
//      * 불량 판단을 ReportInspection(송장 있음, 이름 일치)으로 받는다 (설계서: 판독 스테이션이 정한 box.destination == NG만 본다).
//
//  [점검 11/02] develop 코드 그대로 두고 주석만 달았다. 문제 소지는 [주의], 설계서와 다른 곳은 [설계서와의 차이].
//  ============================================================
[RequireComponent(typeof(Rigidbody)), DisallowMultipleComponent]
public class BoxPusher : MonoBehaviour
{
    //  ---------------- 모드 ----------------
    /// ConveyorTransfer: 옆 벨트로 넘김 / RejectToBin: 수거함으로 떨어뜨림
    public enum PusherMode { ConveyorTransfer, RejectToBin }
    public PusherMode mode;

    //  ---------------- 불량 배출 (RejectToBin) ----------------
    /// 수거함 (있어야 시작) / 수거 확인 영역 중심 · 크기 (월드 좌표)
    [Header("불량 배출")]
    public Transform collectionBin;
    public Vector3 collectionCenter, collectionSize;
    /// 밀기 시작할 때 멈출 원래 벨트. [주의] 다시 켜는 곳이 없다
    public LinearConveyor sourceConveyor;
    /// 수거함에 들어갔는지 기다리는 최대 시간(초)
    [Min(1f)] public float dropTimeout = 5f;
    [Tooltip("수거함 확인 시 한 번 호출됩니다.")]
    public UnityEvent onRejectComplete;
    // 수거 확인 실패 여부
    bool rejectFailed;

    //  ---------------- 옆 벨트로 넘기기 (ConveyorTransfer) ----------------
    /// 밀 상자. [설계서와의 차이] 상자마다 바꿔 넣어야 한다 (두 모드 공통)
    public BoxCollider parcel;
    /// 도착 벨트 (LinearConveyor). Start()와 밀기 시작 때 멈춘다
    public LinearConveyor receiver;
    /// 반대쪽 판. 미는 동안 오브젝트를 꺼 두고 복귀 뒤 되돌린다. 비어 있으면 이름("SeparatorA" ↔ "SeparatorB")으로 찾는다
    [FormerlySerializedAs("separatorA")] public GameObject oppositeSeparator;
    /// 복귀 뒤 도착 벨트를 다시 돌릴지 (default: 끔)
    public bool startReceiverAfterReturn = false;
    /// 미는 방향 (월드 축, default: −Z) / 도착 확인 영역 중심 · 크기 (월드 좌표)
    public Vector3 direction = Vector3.back;
    public Vector3 receiverCenter, receiverSize;
    /// 미는 · 복귀 속도 m/s, 최대 이동 거리 m (default: 0.08 · 0.15 · 1.05)
    [Min(.01f)] public float pushSpeed = .08f, returnSpeed = .15f;
    [Min(.01f)] public float maxTravel = 1.05f;
    /// 넘기기 성공 때 한 번 (수거 성공 때도 함께 부른다)
    public UnityEvent onTransferComplete;

    //  ---------------- 상태 ----------------
    /// 상태 글자 / 동작 중(1 · 2 · 3 · 5단계) / 마지막 동작 성공 / 상자가 도착 영역에 들어갔는지
    [SerializeField] string status = "준비 · Play 후 밀기 시작";
    public string Status => status;
    public bool Busy => state == 1 || state == 2 || state == 3 || state == 5;
    public bool Completed { get; private set; }
    public bool Delivered { get; private set; }
    // body 판 Rigidbody / home 처음 위치 / state 동작 단계 / timer 단계별 시간 / 반대쪽 판을 꺼 뒀는지 · 그 충돌체
    Rigidbody body; Vector3 home; int state; float timer;
    bool separatorWasActive, separatorSuppressed;
    Collider[] separatorColliders;


    //  ============================================================
    //  시작
    //  ============================================================
    // 판을 kinematic으로 두고 처음 위치를 기억한다. 반대쪽 판을 이름으로 찾는다 (ConveyorTransfer).
    /// [주의] GameObject.Find로 이름이 정확히 "SeparatorA" · "SeparatorB"인 오브젝트를 찾는다. 이름이 다르면 못 찾고, 같은 이름이 여럿이면 아무거나 잡힌다.
    void Awake()
    {
        body = GetComponent<Rigidbody>(); 
        body.isKinematic = true; 
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        
        home = body.position;
        if (mode == PusherMode.ConveyorTransfer && oppositeSeparator == null)
            { oppositeSeparator = GameObject.Find(name == "SeparatorA" ? "SeparatorB" : "SeparatorA"); }
        if (oppositeSeparator != null)
            { separatorColliders = oppositeSeparator.GetComponentsInChildren<Collider>(true); }
    }
    
    // 도착 벨트를 멈춘 채로 시작한다.
    void Start() 
    {
        if (receiver != null) { receiver.Stop(); }
    }


    //  ============================================================
    //  명령
    //  ============================================================
    // 검사 결과를 받아, 송장이 없거나 이름이 맞지 않으면 불량 배출을 시작한다 (RejectToBin 모드만).
    // Connect the inspection system here; perception itself is supplied by the caller.
    /// [설계서와의 차이] 공정은 판독 스테이션이 box.destination을 NG로 정하고, 푸셔는 그 값만 본다.
    public void ReportInspection(bool hasShippingLabel, bool labelNameMatches)
    {
        if (!hasShippingLabel || !labelNameMatches) { BeginReject(); }
    }
    
    // 불량 배출 시작 (RejectToBin 모드일 때만 BeginPush()).
    [ContextMenu("불량 가정 배출")]
    public void BeginReject()
    {
        if (mode == PusherMode.RejectToBin) { BeginPush(); }
    }

    // 상자가 수거 확인 영역 안에 들어가 거의 멈췄는지 (속도 0.08 m/s 미만).
    public bool ParcelInsideCollection()
    {
        if (parcel == null || collectionBin == null) { return false; }
        
        Bounds b = parcel.bounds, zone = new Bounds(collectionCenter, collectionSize);
        var rb = parcel.attachedRigidbody;
        return b.min.x >= zone.min.x && b.max.x <= zone.max.x && b.min.z >= zone.min.z && b.max.z <= zone.max.z
            && b.min.y >= zone.min.y - .01f && b.max.y <= zone.max.y && rb != null && rb.linearVelocity.magnitude < .08f;
    }

    // 밀기 시작. 반대쪽 판이 동작 중이거나, 연결이 비었거나, 판이 원위치가 아니면 Status만 바꾸고 끝.
    /// 원래 벨트 · 도착 벨트를 멈추고, 반대쪽 판을 끈 뒤 1단계(밀기)로.
    /// [주의] 판이 상자를 미는 힘은 물리 충돌뿐이다. 상자가 kinematic이면 밀리지 않는다.
    [ContextMenu("밀기 시작")]
    public void BeginPush()
    {
        if (!Application.isPlaying || Busy) { return; }
        
        var otherPusher = oppositeSeparator == null ? null : oppositeSeparator.GetComponent<BoxPusher>();
        if (otherPusher != null && otherPusher.Busy) 
            { status = "반대쪽 판이 동작 중입니다."; return; }
        if (parcel == null || (mode == PusherMode.ConveyorTransfer ? receiver == null : collectionBin == null) || direction.sqrMagnitude < .1f) 
            { status = "박스 / 도착 벨트 / 방향을 확인하세요."; return; }
        if (Vector3.Distance(body.position, home) > .01f) 
            { status = "먼저 판을 원위치로 복귀시키세요."; return; }
        if (sourceConveyor != null) 
            { sourceConveyor.Stop(); }
        if (receiver != null)
            { receiver.Stop(); Completed = false; Delivered = false; rejectFailed = false; state = 1; timer = 0; }
        if (oppositeSeparator != null && !separatorSuppressed)
            { separatorWasActive = oppositeSeparator.activeSelf; oppositeSeparator.SetActive(false); separatorSuppressed = true; }
        
        status = mode == PusherMode.RejectToBin ? "불량 박스를 벨트 밖으로 미는 중" : "박스를 도착 벨트로 미는 중";
    }

    // 판을 원위치로 되돌린다 (3단계). 결과 값은 지운다.
    public void ReturnHome()
    {
        if (!Application.isPlaying) { return; }
        
        Delivered = false; Completed = false; rejectFailed = false;
        if (receiver != null) { receiver.Stop(); }

        state = 3; status = "원위치 복귀 중";
    }

    // 그 자리에서 멈춘다 (4단계).
    /// [주의] 반대쪽 판은 꺼진 채 남는다. ReturnHome()을 불러야 다시 쓸 수 있다.
    public void Stop()
    {
        if (receiver != null)
            { receiver.Stop(); state = 4; status = "정지 · 원위치 복귀로 재설정하세요."; }
    }

    // 상자가 도착 영역 안에 있는지. 밀 때(transferInset)는 상자가 들어오는 쪽 가장자리에서 12 cm 넘게 들어와야 도착으로 본다 (복귀 뒤 확인은 5 mm).
    /// 바닥 높이는 영역 윗면 −4 ~ +6 cm 사이여야 한다.
    /// [주의] 12 cm는 큰 벨트(폭 0.88 m) 기준이다. 폭 0.2 m 벨트와 5 × 6 × 7 cm 상자에서는 영역을 그만큼 넓게 잡아야 도착 판정이 난다.
    public bool ParcelInsideReceiver(bool transferInset = true)
    {
        if (parcel == null) { return false; }

        var b = parcel.bounds; var zone = new Bounds(receiverCenter, receiverSize);
        float inset = transferInset ? .12f : .005f;
        float minX = direction.x > .5f ? inset : .005f, maxX = direction.x < -.5f ? inset : .005f;
        float minZ = direction.z > .5f ? inset : .005f, maxZ = direction.z < -.5f ? inset : .005f;
        return b.min.x >= zone.min.x + minX && b.max.x <= zone.max.x - maxX &&
            b.min.z >= zone.min.z + minZ && b.max.z <= zone.max.z - maxZ &&
            b.min.y > zone.max.y - .04f && b.min.y < zone.max.y + .06f;
    }


    //  ============================================================
    //  동작 (물리 스텝마다)
    //  ============================================================
    // 1 밀기: 도착 영역에 들어가면 2로. 최대 거리나 시간(maxTravel ÷ pushSpeed + 5초)을 넘으면 RejectToBin은 5로, 아니면 정지(4)
    // 5 낙하 확인 → 3 / 2 잠깐(0.3초) 멈춤 → 3 / 3 복귀: 원위치에 오면 0으로, 반대쪽 판을 되돌리고 성공 여부를 정한다
    /// [주의] 1단계 시간 제한이 기본값으로 약 18초다.
    /// [주의] 원래 벨트(sourceConveyor)는 끝나도 다시 켜지 않는다. 도착 벨트는 startReceiverAfterReturn이 켜져 있을 때만 다시 돈다.
    void FixedUpdate()
    {
        if (state == 1)
        {
            timer += Time.fixedDeltaTime;
            if (mode == PusherMode.ConveyorTransfer && ParcelInsideReceiver()) 
                { Delivered = true; state = 2; timer = 0; status = "박스 도착 · 판 정지"; return; }
            if (Vector3.Distance(body.position, home) >= maxTravel || timer > maxTravel / pushSpeed + 5)
            {
                if (mode == PusherMode.RejectToBin) 
                    { state = 5; timer = 0; status = "수거함 낙하 확인 중"; return; }
                Stop(); status = "도착 확인 실패 · 틈/턱 또는 박스 위치를 확인하세요."; return;
            }
            body.MovePosition(
                Vector3.MoveTowards(body.position, home + direction.normalized * maxTravel, pushSpeed * Time.fixedDeltaTime));
        }
        else if (state == 5)
        {
            timer += Time.fixedDeltaTime;
            if (ParcelInsideCollection()) 
                { Delivered = true; state = 3; status = "수거함 도착 확인 · 판 복귀 중"; }
            else if (timer > dropTimeout) 
                { rejectFailed = true; state = 3; status = "수거함 도착 확인 실패 · 판 복귀 중"; }
        }
        else if (state == 2)
        {
            timer += Time.fixedDeltaTime;
            if (timer >= .3f) 
                { state = 3; status = "판 원위치 복귀 중"; }
        }
        else if (state == 3)
        {
            body.MovePosition(Vector3.MoveTowards(body.position, home, returnSpeed * Time.fixedDeltaTime));
            if (Vector3.Distance(body.position, home) < .001f)
            {
                state = 0;
                RestoreSeparator();
                if (mode == PusherMode.RejectToBin)
                {
                    Completed = Delivered && !rejectFailed && ParcelInsideCollection();
                    status = Completed ? "불량 배출 및 판 복귀 완료 · 수거함 안착 확인" : "불량 배출 실패 · 수거함과 낙하 경로를 확인하세요.";
                    if (Completed) 
                        { onRejectComplete?.Invoke(); onTransferComplete?.Invoke(); }
                    return;
                }
                if (Delivered && ParcelInsideReceiver(false))
                {
                    if (startReceiverAfterReturn) { receiver.Play(); }
                    else 
                    { 
                        receiver.Stop(); 
                        Completed = true; 
                        status = startReceiverAfterReturn ? "복귀 완료 · 도착 벨트 가동" : "이송 및 판 복귀 완료 · 도착 벨트 정지 유지"; 
                        onTransferComplete?.Invoke(); 
                    }
                }
                else { status = "원위치 복귀 완료 · 도착 벨트는 정지 상태"; }
            }
        }
    }

    // 반대쪽 판을 원래 상태로 켠다. 상자가 그 판과 겹쳐 있으면 켜지 않는다.
    /// [주의] 겹쳐서 못 켜면 꺼진 채 남고, 다음 동작이 끝날 때 다시 확인한다.
    void RestoreSeparator()
    {
        if (!separatorSuppressed || oppositeSeparator == null) { return; }
        if (separatorWasActive && parcel != null && separatorColliders != null)
            foreach (var c in separatorColliders)
                if (c != null && c.enabled && !c.isTrigger &&
                    Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, parcel, parcel.transform.position, parcel.transform.rotation, out _, out _)
                ) { return; }
        oppositeSeparator.SetActive(separatorWasActive); 
        separatorSuppressed = false;
    }
    // 선택하면 확인 영역(하늘색)과 미는 방향 · 거리(노란 선)를 그린다.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (mode == PusherMode.RejectToBin) { Gizmos.DrawWireCube(collectionCenter, collectionSize); }
        else { Gizmos.DrawWireCube(receiverCenter, receiverSize); }

        Gizmos.color = Color.yellow; 
        Gizmos.DrawLine(transform.position, transform.position + direction.normalized * maxTravel);
    }
}
