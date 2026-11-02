using System;
using System.Linq;
using System.Collections;
using UnityEngine;

// Owns the 2 x 2 x 2 packing order and physical support checks.
[DisallowMultipleComponent]
public sealed class ToteBase : MonoBehaviour
{
    public BoxAttacher loader;
    public Transform[] slots = new Transform[8];
    public Vector3 parcelSize = new Vector3(.05f, .06f, .07f);
    public float positionTolerance = .008f;
    [Tooltip("박스 밑면과 수거함 테두리 사이의 흡착 해제 높이(m)")]
    [Range(.01f, .05f)] public float releaseClearance = .015f;
    public string Status = "아래층 4개 → 위층 4개";
    [Header("반복 적재 시험")]
    public GameObject testParcelPrefab;
    [Tooltip("다음 시험 박스를 생성할 때 5×6면이 월드 X/Y/Z 중 무작위 방향을 향합니다. 첫 박스는 현재 배치를 사용합니다.")]
    public bool randomizeSpawnAxis = true;
    [SerializeField] string lastSpawnAxis = "첫 박스는 현재 배치";
    [Min(.1f)] public float nextBoxDelay = .8f;
    public bool AutoStacking => autoRoutine != null;
    Coroutine autoRoutine;
    GameObject testTemplate;
    readonly BoxCollider[] boxes = new BoxCollider[8];
    public int Capacity => 8;
    public int Count => boxes.Count(b => b != null);
    public BoxCollider[] Boxes => (BoxCollider[])boxes.Clone();

    public void StackNextBox()
    {
        if (loader == null) { Status = "흡착 로봇을 연결하세요."; return; }
        loader.toto = this; loader.container = transform; loader.slots = slots;
        loader.LoadNextBox();
    }
    public void StartAutoStack()
    {
        if (!Application.isPlaying || AutoStacking || loader == null || loader.Busy || loader.Attached || Count >= Capacity) return;
        if (loader.targetBox == null || !IsCorrectSize(loader.targetBox))
        { Status = "집기 위치의 5×6×7cm 박스를 지정하세요."; return; }
        autoRoutine = StartCoroutine(AutoStack());
    }
    public void StopAutoStack()
    {
        if (autoRoutine != null) StopCoroutine(autoRoutine);
        autoRoutine = null;
        if (loader != null) loader.StopLoading();
        if (testTemplate != null) Destroy(testTemplate);
        testTemplate = null;
        Status = loader != null && loader.Attached ? "반복 중단 · 흡착 유지" : "반복 중단";
    }
    IEnumerator AutoStack()
    {
        yield return null;
        var source = loader.targetBox;
        Vector3 pickup = source.bounds.center;
        float beltTop = source.bounds.min.y;
        Quaternion rotation = source.transform.rotation;
        if (testParcelPrefab == null)
        {
            testTemplate = Instantiate(source.gameObject);
            testTemplate.name = "ParcelSpawnTemplate";
            testTemplate.SetActive(false);
            testTemplate.hideFlags = HideFlags.HideAndDontSave;
        }
        while (Count < Capacity)
        {
            int before = Count;
            Status = "자동 적재 · " + (before + 1) + "/8";
            StackNextBox();
            yield return null;
            while (loader.Busy) yield return null;
            if (Count != before + 1 || !loader.LastLoadSucceeded || loader.Attached)
            { Status = "자동 적재 중단 · " + loader.Status; break; }
            if (Count >= Capacity) { Status = "8/8 적재 완료"; break; }
            yield return new WaitForSeconds(nextBoxDelay);
            // A new parcel appears only after placement and tool retraction have succeeded.
            var prefab = testParcelPrefab != null ? testParcelPrefab : testTemplate;
            // These are the three independently tested orientations. Local +/-Z is always the 5x6 face.
            int axis = randomizeSpawnAxis ? UnityEngine.Random.Range(0, 3) : -1;
            Quaternion spawnRotation = axis == 0 ? Quaternion.Euler(0f, 90f, 0f)
                : axis == 1 ? Quaternion.Euler(90f, 0f, 0f)
                : axis == 2 ? Quaternion.identity : rotation;
            lastSpawnAxis = axis == 0 ? "X" : axis == 1 ? "Y" : axis == 2 ? "Z" : "시작 방향 유지";
            var next = Instantiate(prefab, pickup, spawnRotation);
            next.hideFlags = HideFlags.None;
            next.name = "ParcelStackTest_" + (Count + 1);
            next.SetActive(true);
            Physics.SyncTransforms();
            var box = next.GetComponent<BoxCollider>();
            var body = box != null ? box.attachedRigidbody : null;
            if (box == null || body == null || !IsCorrectSize(box))
            { Destroy(next); Status = "자동 적재 중단 · 생성 프리팹의 Collider/Rigidbody/크기 확인"; break; }
            next.transform.position += new Vector3(pickup.x - box.bounds.center.x, beltTop + box.bounds.extents.y + .001f - box.bounds.center.y, pickup.z - box.bounds.center.z);
            body.position = next.transform.position;
            body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            loader.targetBox = box;
            yield return new WaitForSeconds(.3f);
        }
        if (testTemplate != null) Destroy(testTemplate);
        testTemplate = null; autoRoutine = null;
    }
    public float RimHeight => GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).Max(c => c.bounds.max.y);
    public Vector3 DropCentre(BoxCollider box, Vector3 slotCentre)
    {
        slotCentre.y = RimHeight + Mathf.Clamp(releaseClearance, .01f, .05f) + box.bounds.extents.y;
        return slotCentre;
    }
    public bool ReadyToRelease(int index, BoxCollider box, Vector3 centre)
    {
        if (box == null || !Upright(box) || index < 0 || index >= Capacity) return false;
        Vector3 delta = transform.InverseTransformVector(box.bounds.center - centre);
        if (Mathf.Abs(delta.x) > positionTolerance || Mathf.Abs(delta.z) > positionTolerance) return false;
        float bottom = box.bounds.min.y;
        float rim = RimHeight;
        float wantedBottom = rim + Mathf.Clamp(releaseClearance, .01f, .05f);
        if (bottom < rim + .006f || Mathf.Abs(bottom - wantedBottom) > .004f) return false;
        var body = box.attachedRigidbody;
        if (body != null && (new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude > .025f || body.angularVelocity.magnitude > .3f)) return false;
        var support = index >= 4 ? boxes[index - 4] : null;
        if (index >= 4 && (support == null || !IsSettledAt(index - 4, support))) return false;
        // The first surface under this slot must be the tote or its registered lower box.
        foreach (var hit in Physics.RaycastAll(box.bounds.center, Vector3.down, Mathf.Max(.1f, box.bounds.center.y - centre.y + parcelSize.z), ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (hit.collider.attachedRigidbody == body || hit.collider.transform.IsChildOf(loader.transform)) continue;
            bool valid = index < 4 ? hit.collider.transform.IsChildOf(transform) : hit.collider.attachedRigidbody == support.attachedRigidbody;
            return valid && Vector3.Dot(hit.normal, Vector3.up) > .9f;
        }
        return false;
    }
    public bool IsSettledAt(int index, BoxCollider box) => box != null && index >= 0 && index < Capacity && Upright(box) && NearSlot(box, index) && Settled(box);
    void OnDisable()
    {
        if (autoRoutine != null) StopCoroutine(autoRoutine);
        autoRoutine = null;
        if (testTemplate != null) Destroy(testTemplate);
        testTemplate = null;
    }
    public bool IsCorrectSize(BoxCollider box)
    {
        if (box == null) return false;
        Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
        return (size - parcelSize).sqrMagnitude < .000001f;
    }
    public Vector3 SuctionPoint(BoxCollider box, float sign)
    {
        Vector3 normal = box.transform.forward.normalized * sign;
        Vector3 tangent = Vector3.ProjectOnPlane(Vector3.up, normal);
        Vector3 centre = box.transform.TransformPoint(box.center + Vector3.forward * box.size.z * .5f * sign);
        if (tangent.sqrMagnitude < .01f) return centre;
        tangent.Normalize();
        float halfSpan = (Mathf.Abs(Vector3.Dot(box.transform.right, tangent)) * parcelSize.x + Mathf.Abs(Vector3.Dot(box.transform.up, tangent)) * parcelSize.y) * .5f;
        return centre + tangent * Mathf.Min(.018f, Mathf.Max(0, halfSpan - .01f));
    }
    public Quaternion PlacementRotation(float faceSign)
    {
        // Local X=5 cm, local Y=6 cm, local Z=7 cm. The chosen +/-Z face points up.
        return Quaternion.LookRotation(transform.up * faceSign, -transform.forward * faceSign);
    }
    public bool TryNext(BoxCollider own, out int index, out Vector3 centre, out string reason)
    {
        index = -1; centre = Vector3.zero; reason = "";
        if (slots == null || slots.Length != Capacity || slots.Any(s => s == null))
        { reason = "2×2×2 적재 슬롯 8개를 연결하세요."; return false; }
        if (!IsCorrectSize(own)) { reason = "5×6×7cm, Scale 1의 박스만 적재할 수 있습니다."; return false; }
        for (int i = 0; i < Capacity; i++)
        {
            if (boxes[i] != null) continue;
            index = i; centre = slots[i].position;
            if (i >= 4)
            {
                var support = boxes[i - 4];
                if (support == null || !Upright(support) || !NearSlot(support, i - 4) || !Settled(support))
                { reason = "위층 적재 중단 · 바로 아래 박스의 위치/자세/안정을 확인하세요."; return false; }
                centre.y = support.bounds.max.y + parcelSize.z * .5f + .001f;
            }
            Vector3 extents = new Vector3(parcelSize.x, parcelSize.z, parcelSize.y) * .4f;
            var occupied = Physics.OverlapBox(centre, extents, transform.rotation, ~0, QueryTriggerInteraction.Ignore)
                .Any(c => c != own && c.attachedRigidbody != null && c.attachedRigidbody != own.attachedRigidbody && !c.transform.IsChildOf(loader.transform));
            if (occupied) { reason = "적재 위치에 다른 박스가 있습니다."; return false; }
            return true;
        }
        reason = "수거함 가득 참 · 8/8"; return false;
    }
    public bool Upright(BoxCollider box) => Mathf.Abs(Vector3.Dot(box.transform.forward.normalized, transform.up)) > .985f;
    bool NearSlot(BoxCollider box, int index)
    {
        var delta = transform.InverseTransformVector(box.bounds.center - slots[index].position);
        return Mathf.Abs(delta.x) <= positionTolerance && Mathf.Abs(delta.z) <= positionTolerance && Mathf.Abs(delta.y) <= .012f;
    }
    static bool Settled(BoxCollider box) => box.attachedRigidbody == null || (box.attachedRigidbody.linearVelocity.magnitude < .04f && box.attachedRigidbody.angularVelocity.magnitude < .3f);
    public bool Register(int index, BoxCollider box)
    {
        if (index < 0 || index >= Capacity || boxes[index] != null || !Upright(box) || !NearSlot(box, index) || !Settled(box))
        { Status = "적재 확인 실패 · 박스가 안정된 위치에 있지 않습니다."; return false; }
        boxes[index] = box; Status = "적재 완료 · " + Count + "/8 · " + (index < 4 ? "아래층" : "위층") + " " + (index % 4 + 1);
        return true;
    }
    void OnDrawGizmosSelected()
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++) if (slots[i] != null)
        {
            Gizmos.color = i < 4 ? Color.cyan : Color.yellow;
            Gizmos.matrix = Matrix4x4.TRS(slots[i].position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(parcelSize.x, parcelSize.z, parcelSize.y));
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
