using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using RoboLacyTech.Conveyors;

[RequireComponent(typeof(Rigidbody)), DisallowMultipleComponent]
public class BoxPusher : MonoBehaviour
{
    public enum PusherMode { ConveyorTransfer, RejectToBin }
    public PusherMode mode;
    [Header("불량 배출")]
    public Transform collectionBin;
    public Vector3 collectionCenter, collectionSize;
    public LinearConveyor sourceConveyor;
    [Min(1f)] public float dropTimeout = 5f;
    [Tooltip("수거함 확인 시 한 번 호출됩니다.")]
    public UnityEvent onRejectComplete;
    bool rejectFailed;
    public BoxCollider parcel;
    public LinearConveyor receiver;
    [FormerlySerializedAs("separatorA")] public GameObject oppositeSeparator;
    public bool startReceiverAfterReturn = false;
    public Vector3 direction = Vector3.back;
    public Vector3 receiverCenter, receiverSize;
    [Min(.01f)] public float pushSpeed = .08f, returnSpeed = .15f;
    [Min(.01f)] public float maxTravel = 1.05f;
    public UnityEvent onTransferComplete;
    [SerializeField] string status = "준비 · Play 후 밀기 시작";
    public string Status => status;
    public bool Busy => state == 1 || state == 2 || state == 3 || state == 5;
    public bool Completed { get; private set; }
    public bool Delivered { get; private set; }
    Rigidbody body; Vector3 home; int state; float timer;
    bool separatorWasActive, separatorSuppressed;
    Collider[] separatorColliders;
    void Awake()
    {
        body = GetComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        home = body.position;
        if (mode == PusherMode.ConveyorTransfer && oppositeSeparator == null) oppositeSeparator = GameObject.Find(name == "SeparatorA" ? "SeparatorB" : "SeparatorA");
        if (oppositeSeparator != null) separatorColliders = oppositeSeparator.GetComponentsInChildren<Collider>(true);
    }
    void Start() { if (receiver != null) receiver.Stop(); }
    // Connect the inspection system here; perception itself is supplied by the caller.
    public void ReportInspection(bool hasShippingLabel, bool labelNameMatches)
    { if (!hasShippingLabel || !labelNameMatches) BeginReject(); }
    [ContextMenu("불량 가정 배출")]
    public void BeginReject()
    { if (mode == PusherMode.RejectToBin) BeginPush(); }
    public bool ParcelInsideCollection()
    {
        if (parcel == null || collectionBin == null) return false;
        Bounds b = parcel.bounds, zone = new Bounds(collectionCenter, collectionSize);
        var rb = parcel.attachedRigidbody;
        return b.min.x >= zone.min.x && b.max.x <= zone.max.x && b.min.z >= zone.min.z && b.max.z <= zone.max.z
            && b.min.y >= zone.min.y - .01f && b.max.y <= zone.max.y && rb != null && rb.linearVelocity.magnitude < .08f;
    }
    [ContextMenu("밀기 시작")]
    public void BeginPush()
    {
        if (!Application.isPlaying || Busy) return;
        var otherPusher = oppositeSeparator == null ? null : oppositeSeparator.GetComponent<BoxPusher>();
        if (otherPusher != null && otherPusher.Busy) { status = "반대쪽 판이 동작 중입니다."; return; }
        if (parcel == null || (mode == PusherMode.ConveyorTransfer ? receiver == null : collectionBin == null) || direction.sqrMagnitude < .1f) { status = "박스 / 도착 벨트 / 방향을 확인하세요."; return; }
        if (Vector3.Distance(body.position, home) > .01f) { status = "먼저 판을 원위치로 복귀시키세요."; return; }
        if (sourceConveyor != null) sourceConveyor.Stop();
        if (receiver != null) receiver.Stop(); Completed = false; Delivered = false; rejectFailed = false; state = 1; timer = 0;
        if (oppositeSeparator != null && !separatorSuppressed)
        { separatorWasActive = oppositeSeparator.activeSelf; oppositeSeparator.SetActive(false); separatorSuppressed = true; }
        status = mode == PusherMode.RejectToBin ? "불량 박스를 벨트 밖으로 미는 중" : "박스를 도착 벨트로 미는 중";
    }
    public void ReturnHome()
    {
        if (!Application.isPlaying) return;
        Delivered = false; Completed = false; rejectFailed = false; if (receiver != null) receiver.Stop();
        state = 3; status = "원위치 복귀 중";
    }
    public void Stop()
    { if (receiver != null) receiver.Stop(); state = 4; status = "정지 · 원위치 복귀로 재설정하세요."; }
    public bool ParcelInsideReceiver(bool transferInset = true)
    {
        if (parcel == null) return false;
        var b = parcel.bounds; var zone = new Bounds(receiverCenter, receiverSize);
        float inset = transferInset ? .12f : .005f;
        float minX = direction.x > .5f ? inset : .005f, maxX = direction.x < -.5f ? inset : .005f;
        float minZ = direction.z > .5f ? inset : .005f, maxZ = direction.z < -.5f ? inset : .005f;
        return b.min.x >= zone.min.x + minX && b.max.x <= zone.max.x - maxX &&
            b.min.z >= zone.min.z + minZ && b.max.z <= zone.max.z - maxZ &&
            b.min.y > zone.max.y - .04f && b.min.y < zone.max.y + .06f;
    }
    void FixedUpdate()
    {
        if (state == 1)
        {
            timer += Time.fixedDeltaTime;
            if (mode == PusherMode.ConveyorTransfer && ParcelInsideReceiver()) { Delivered = true; state = 2; timer = 0; status = "박스 도착 · 판 정지"; return; }
            if (Vector3.Distance(body.position, home) >= maxTravel || timer > maxTravel / pushSpeed + 5)
            {
                if (mode == PusherMode.RejectToBin) { state = 5; timer = 0; status = "수거함 낙하 확인 중"; return; }
                Stop(); status = "도착 확인 실패 · 틈/턱 또는 박스 위치를 확인하세요."; return; }
            body.MovePosition(Vector3.MoveTowards(body.position, home + direction.normalized * maxTravel, pushSpeed * Time.fixedDeltaTime));
        }
        else if (state == 5)
        {
            timer += Time.fixedDeltaTime;
            if (ParcelInsideCollection()) { Delivered = true; state = 3; status = "수거함 도착 확인 · 판 복귀 중"; }
            else if (timer > dropTimeout) { rejectFailed = true; state = 3; status = "수거함 도착 확인 실패 · 판 복귀 중"; }
        }
        else if (state == 2)
        {
            timer += Time.fixedDeltaTime;
            if (timer >= .3f) { state = 3; status = "판 원위치 복귀 중"; }
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
                    if (Completed) { onRejectComplete?.Invoke(); onTransferComplete?.Invoke(); }
                    return;
                }
                if (Delivered && ParcelInsideReceiver(false))
                { if (startReceiverAfterReturn) receiver.Play(); else receiver.Stop(); Completed = true; status = startReceiverAfterReturn ? "복귀 완료 · 도착 벨트 가동" : "이송 및 판 복귀 완료 · 도착 벨트 정지 유지"; onTransferComplete?.Invoke(); }
                else { status = "원위치 복귀 완료 · 도착 벨트는 정지 상태"; }
            }
        }
    }
    void RestoreSeparator()
    {
        if (!separatorSuppressed || oppositeSeparator == null) return;
        if (separatorWasActive && parcel != null && separatorColliders != null)
            foreach (var c in separatorColliders)
                if (c != null && c.enabled && !c.isTrigger && Physics.ComputePenetration(c, c.transform.position, c.transform.rotation,
                    parcel, parcel.transform.position, parcel.transform.rotation, out _, out _)) return;
        oppositeSeparator.SetActive(separatorWasActive); separatorSuppressed = false;
    }
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan; if (mode == PusherMode.RejectToBin) Gizmos.DrawWireCube(collectionCenter, collectionSize); else Gizmos.DrawWireCube(receiverCenter, receiverSize);
        Gizmos.color = Color.yellow; Gizmos.DrawLine(transform.position, transform.position + direction.normalized * maxTravel);
    }
}
