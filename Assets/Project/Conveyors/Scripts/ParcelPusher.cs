using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using RoboLacyTech.Conveyors;

[RequireComponent(typeof(Rigidbody)), DisallowMultipleComponent]
public class ParcelPusher : MonoBehaviour
{
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
    public bool Busy => state == 1 || state == 2 || state == 3;
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
        if (oppositeSeparator == null) oppositeSeparator = GameObject.Find(name == "SeparatorA" ? "SeparatorB" : "SeparatorA");
        if (oppositeSeparator != null) separatorColliders = oppositeSeparator.GetComponentsInChildren<Collider>(true);
    }
    void Start() { if (receiver != null) receiver.Stop(); }
    [ContextMenu("밀기 시작")]
    public void BeginPush()
    {
        if (!Application.isPlaying || Busy) return;
        var otherPusher = oppositeSeparator == null ? null : oppositeSeparator.GetComponent<ParcelPusher>();
        if (otherPusher != null && otherPusher.Busy) { status = "반대쪽 판이 동작 중입니다."; return; }
        if (parcel == null || receiver == null || direction.sqrMagnitude < .1f) { status = "박스 / 도착 벨트 / 방향을 확인하세요."; return; }
        if (Vector3.Distance(body.position, home) > .01f) { status = "먼저 판을 원위치로 복귀시키세요."; return; }
        receiver.Stop(); Completed = false; Delivered = false; state = 1; timer = 0;
        if (oppositeSeparator != null && !separatorSuppressed)
        { separatorWasActive = oppositeSeparator.activeSelf; oppositeSeparator.SetActive(false); separatorSuppressed = true; }
        status = "박스를 도착 벨트로 미는 중";
    }
    public void ReturnHome()
    {
        if (!Application.isPlaying) return;
        Delivered = false; Completed = false; if (receiver != null) receiver.Stop();
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
            if (ParcelInsideReceiver()) { Delivered = true; state = 2; timer = 0; status = "박스 도착 · 판 정지"; return; }
            if (Vector3.Distance(body.position, home) >= maxTravel || timer > maxTravel / pushSpeed + 5)
            { Stop(); status = "도착 확인 실패 · 틈/턱 또는 박스 위치를 확인하세요."; return; }
            body.MovePosition(Vector3.MoveTowards(body.position, home + direction.normalized * maxTravel, pushSpeed * Time.fixedDeltaTime));
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
        Gizmos.color = Color.cyan; Gizmos.DrawWireCube(receiverCenter, receiverSize);
        Gizmos.color = Color.yellow; Gizmos.DrawLine(transform.position, transform.position + direction.normalized * maxTravel);
    }
}
