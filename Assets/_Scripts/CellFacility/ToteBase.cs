using System;
using System.Linq;
using System.Collections;
using UnityEngine;

//  ============================================================
//  ToteBase
//  터미널 컨테이너. 2 × 2 × 2 칸의 적재 순서와, 상자를 놓을 자리 · 놓아도 되는지를 정한다.
//      - 아래층 4칸 → 위층 4칸 순서로 다음 칸을 고른다 (TryNext). 위층 칸은 바로 아래 상자가 바르게 놓여 멈춰 있을 때만 고른다.
//      - 흡착 로봇(BoxAttacher)이 흡착 지점 · 놓는 회전 · 놓는 높이를 여기서 받고, 놓은 뒤 Register()로 칸에 등록한다.
//      - 반복 적재 시험: 같은 자리에 다음 상자를 만들어 8칸을 연속으로 채워 본다 (Inspector 버튼).
//
//  [상자 축] 로컬 X = 5 cm, Y = 6 cm, Z = 7 cm. 로컬 ±Z 면(5 × 6 면)만 흡착하고, 그 면이 위를 보게 놓는다.
//  [칸 번호] 0 ~ 3 = 아래층, 4 ~ 7 = 위층. 칸 i + 4가 칸 i의 바로 위 칸이다.
//  [사용] 컨테이너 오브젝트(ParcelContainer_2x2x2_8)에 붙이고, Slots 8칸과 Loader(그 터미널 흡착 로봇의 BoxAttacher)를 연결한다.
//         봉현님 도구 Tools/Bonguri/Configure Suction Loading이 칸 · 연결을 맞춘다.
//  [!] 5 × 6 × 7 cm, Scale 1인 상자만 받는다 (IsCorrectSize).
//  [!] Boxes는 복사본이다. 칸 번호 순서이고, 빈 칸 · 지워진 상자는 null이다.
//
//  [설계서와의 차이]
//      * 설계서: 적재 칸 계산 공통 부모 (abstract, 3 × 3, NextSlot() · Add()), NgTote · AgvContainer가 물려받는다.
//        현재 구현은 sealed 단독 클래스이고 2 × 2 × 2 · 8칸이다. NgTote는 칸 계산을 따로 갖는다 (11/03 팀 원칙).
//      * Add() 대신 Register(index, box)로 등록한다. 칸 자리 · 자세 · 멈춤을 확인하고 받는다.
//      * 가득 차면 비우기는 없다. LoadSequence가 상자를 지워 비운다.
//      * 반복 적재 시험(StartAutoStack())이 들어 있다.
//  ============================================================
[DisallowMultipleComponent]
public sealed class ToteBase : MonoBehaviour
{
    //  ---------------- 연결 ----------------    (FR-08)
    [Header("연결")]
    [Tooltip("이 컨테이너에 넣는 흡착 로봇 (같은 터미널의 BoxAttacher)")]
    public BoxAttacher loader;

    [Tooltip("적재 칸 8개 (칸 가운데 위치). 0 ~ 3 = 아래층, 4 ~ 7 = 위층")]
    public Transform[] slots = new Transform[8];

    //  ---------------- 상자 · 놓기 ----------------
    [Header("상자 · 놓기")]
    [Tooltip("받는 상자 크기 m (로컬 X, Y, Z). Collider 크기 × Scale이 이 값과 같은 상자만 받는다 (default: 0.05, 0.06, 0.07)")]
    public Vector3 parcelSize = new Vector3(.05f, .06f, .07f);

    [Tooltip("칸 가운데에서 벗어나도 되는 수평 거리 m. 놓기 직전 · 등록할 때 본다 (default: 0.008)")]
    public float positionTolerance = .008f;

    [Tooltip("흡착을 놓는 높이 m. 상자 밑면과 컨테이너 테두리 사이 간격 (default: 0.015)")]
    [Range(.01f, .05f)] public float releaseClearance = .015f;

    //  ---------------- 상태 ----------------
    [Header("상태")]
    [Tooltip("지금 상태 문구 (적재 완료 · 확인 실패 · 시험 진행). 확인용")]
    public string Status = "아래층 4개 → 위층 4개";

    //  ---------------- 반복 적재 시험 ----------------
    [Header("반복 적재 시험")]
    [Tooltip("(선택) 다음 시험 상자로 만들 Prefab. 비우면 첫 상자를 복제해 쓴다")]
    public GameObject testParcelPrefab;

    [Tooltip("다음 시험 상자의 5 × 6 면이 월드 X · Y · Z 중 무작위 방향을 향하게 만든다. 첫 상자는 놓인 그대로 쓴다 (default: 켜짐)")]
    public bool randomizeSpawnAxis = true;

    [Tooltip("마지막으로 만든 시험 상자의 5 × 6 면 방향 (확인용)")]
    [SerializeField] string lastSpawnAxis = "첫 박스는 현재 배치";

    [Tooltip("상자 하나를 넣은 뒤 다음 시험 상자를 만들기까지 기다리는 시간 초 (default: 0.8)")]
    [Min(.1f)] public float nextBoxDelay = .8f;


    //  ============================================================
    //  상태 (BoxAttacher · LoadSequence · 에디터 도구가 읽는 값)
    //  ============================================================
    /// 칸 수 (2 × 2 × 2), 들어 있는 상자 수 (지워진 상자는 빼고 센다)
    public int Capacity => 8;
    public int Count => boxes.Count(b => b != null);

    /// 칸별 상자의 복사본 (칸 번호 순서, 빈 칸은 null)
    public BoxCollider[] Boxes => (BoxCollider[])boxes.Clone();

    /// 반복 적재 시험 중인지
    public bool AutoStacking => autoRoutine != null;

    /// 컨테이너 테두리 높이 (월드 y). 이 오브젝트와 자식의 켜진 Collider(트리거 제외) 중 가장 높은 윗면
    public float RimHeight => GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).Max(c => c.bounds.max.y);


    //  ============================================================
    //  안쪽 값
    //  ============================================================
    readonly BoxCollider[] boxes = new BoxCollider[8];      //  칸별로 등록한 상자 (Register()가 채운다)
    Coroutine autoRoutine;                                  //  반복 적재 시험 코루틴
    GameObject testTemplate;                                //  시험 상자 복제 원본 (숨겨 두고, 시험이 끝나면 지운다)


    //  ============================================================
    //  칸 고르기 · 등록 (BoxAttacher가 부름)
    //  ============================================================
    // 다음에 넣을 칸 번호(index)와 그 칸에 놓을 상자 가운데 위치(centre)를 고른다. 못 넣으면 이유를 reason에 넣고 false.
    /// [호출] BoxAttacher (흡착하기 전, 상자를 돌린 뒤 놓으러 가기 전에 한 번 더)
    /// 빈 칸 중 번호가 가장 작은 칸을 고른다. 위층 칸은 바로 아래 상자가 바르게 놓여 멈춰 있어야 하고, 높이를 그 상자 윗면에 맞춘다.
    /// [!] 칸 자리에 다른 상자가 있으면 false. 들고 있는 상자(own)와 흡착 로봇은 빼고 본다.
    public bool TryNext(BoxCollider own, out int index, out Vector3 centre, out string reason)
    {
        index = -1;
        centre = Vector3.zero;
        reason = "";

        //  1) 칸 8개 · 상자 크기 확인
        if (slots == null || slots.Length != Capacity || slots.Any(s => s == null))
            { reason = "2×2×2 적재 슬롯 8개를 연결하세요."; return false; }
        if (!IsCorrectSize(own)) 
            { reason = "5×6×7cm, Scale 1의 박스만 적재할 수 있습니다."; return false; }

        //  2) 빈 칸 중 첫 칸을 고른다
        for (int i = 0; i < Capacity; i++)
        {
            if (boxes[i] != null) { continue; }
            index = i;
            centre = slots[i].position;

            //  위층 칸: 바로 아래 상자가 칸에 바르게 놓여 멈춰 있어야 한다. 높이 = 아래 상자 윗면 + 상자 높이(7 cm) 절반 + 1 mm
            if (i >= 4)
            {
                var support = boxes[i - 4];
                if (support == null || !Upright(support) || !NearSlot(support, i - 4) || !Settled(support))
                    { reason = "위층 적재 중단 · 바로 아래 박스의 위치/자세/안정을 확인하세요."; return false; }
                centre.y = support.bounds.max.y + parcelSize.z * .5f + .001f;
            }

            //  3) 칸 자리에 다른 상자가 있는지 (상자 크기의 80%로 겹침 검사, 7 cm 변이 세로)
            Vector3 extents = new Vector3(parcelSize.x, parcelSize.z, parcelSize.y) * .4f;
            var occupied = Physics.OverlapBox(centre, extents, transform.rotation, ~0, QueryTriggerInteraction.Ignore).Any(c => 
                                c != own && c.attachedRigidbody 
                                  != null && c.attachedRigidbody 
                                  != own.attachedRigidbody && !c.transform.IsChildOf(loader.transform));
            if (occupied) 
                { reason = "적재 위치에 다른 박스가 있습니다."; return false; }
            return true;
        }
        reason = "수거함 가득 참 · 8/8";
        return false;
    }

    // 놓은 상자를 index 칸에 등록한다. 칸이 비어 있고, 상자가 칸 자리에 바르게 놓여 멈춰 있어야 받는다.
    /// [호출] BoxAttacher (흡착을 놓고 팔을 올린 뒤, 상자가 칸에 멈추면)
    /// 받으면 Status에 "적재 완료 · n/8 · 층 칸", 못 받으면 "적재 확인 실패"를 쓴다.
    public bool Register(int index, BoxCollider box)
    {
        if (index < 0 || index >= Capacity || boxes[index] != null || !Upright(box) || !NearSlot(box, index) || !Settled(box))
            { Status = "적재 확인 실패 · 박스가 안정된 위치에 있지 않습니다."; return false; }
        boxes[index] = box;
        Status = "적재 완료 · " + Count + "/8 · " + (index < 4 ? "아래층" : "위층") + " " + (index % 4 + 1);
        return true;
    }


    //  ============================================================
    //  흡착 · 놓기 자리 (BoxAttacher가 부름)
    //  ============================================================
    // 흡착할 지점 (월드 위치). 상자의 sign 쪽 5 × 6 면(로컬 ±Z) 가운데에서, 면을 따라 위쪽으로 조금 옮긴 점.
    /// [호출] BoxAttacher, TotoRobotPlacement (에디터 도구)
    /// 면이 옆을 보면 위쪽으로 최대 1.8 cm 옮긴다 (면 끝에서 1 cm 안쪽까지만). 면이 위 · 아래를 보면 가운데 그대로.
    public Vector3 SuctionPoint(BoxCollider box, float sign)
    {
        Vector3 normal = box.transform.forward.normalized * sign;
        Vector3 tangent = Vector3.ProjectOnPlane(Vector3.up, normal);
        Vector3 centre = box.transform.TransformPoint(box.center + Vector3.forward * box.size.z * .5f * sign);
        if (tangent.sqrMagnitude < .01f) { return centre; }
        tangent.Normalize();
        float halfSpan = (Mathf.Abs(Vector3.Dot(box.transform.right, tangent)) * parcelSize.x + Mathf.Abs(Vector3.Dot(box.transform.up, tangent)) * parcelSize.y) * .5f;
        return centre + tangent * Mathf.Min(.018f, Mathf.Max(0, halfSpan - .01f));
    }

    // 상자를 놓을 때의 회전. faceSign 쪽 5 × 6 면(로컬 ±Z)이 위를 보고, 나머지 축은 컨테이너 축에 맞춘다.
    /// [호출] BoxAttacher (흡착 뒤 돌릴 목표 자세)
    public Quaternion PlacementRotation(float faceSign)
    {
        //  로컬 X = 5 cm, Y = 6 cm, Z = 7 cm. 고른 ±Z 면이 위를 본다
        return Quaternion.LookRotation(transform.up * faceSign, -transform.forward * faceSign);
    }

    // 흡착을 놓을 때 상자 가운데 위치. 칸 위치(slotCentre)에서 높이만 "테두리 + releaseClearance + 상자 높이 절반"으로 바꾼다.
    /// [호출] BoxAttacher (놓는 높이 계산)
    public Vector3 DropCentre(BoxCollider box, Vector3 slotCentre)
    {
        slotCentre.y = RimHeight + Mathf.Clamp(releaseClearance, .01f, .05f) + box.bounds.extents.y;
        return slotCentre;
    }

    // 흡착을 놓아도 되는지. 상자가 바로 서 있고, 칸 위 정해진 높이에 멈춰 있고, 바로 아래가 컨테이너(아래층) 또는 등록된 아래 상자(위층)이면 true.
    /// [호출] BoxAttacher (내려가는 동안 매 물리 프레임. 0.15초 이어서 true면 흡착을 놓는다)
    /// [기준] 수평 오차 positionTolerance 이하, 밑면 = 테두리 + releaseClearance ± 4 mm (테두리 + 6 mm보다 위),
    ///        수평 속도 0.025 m/s 이하, 회전 속도 0.3 rad/s 이하.
    public bool ReadyToRelease(int index, BoxCollider box, Vector3 centre)
    {
        if (box == null || !Upright(box) || index < 0 || index >= Capacity) { return false; }

        //  1) 칸 가운데에서 수평으로 벗어났는지 (컨테이너 축 기준)
        Vector3 delta = transform.InverseTransformVector(box.bounds.center - centre);
        if (Mathf.Abs(delta.x) > positionTolerance || Mathf.Abs(delta.z) > positionTolerance) { return false; }

        //  2) 밑면 높이
        float bottom = box.bounds.min.y;
        float rim = RimHeight;
        float wantedBottom = rim + Mathf.Clamp(releaseClearance, .01f, .05f);
        if (bottom < rim + .006f || Mathf.Abs(bottom - wantedBottom) > .004f) { return false; }

        //  3) 흔들림
        var body = box.attachedRigidbody;
        if (body != null && (new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude > .025f || body.angularVelocity.magnitude > .3f)) 
            { return false; }

        //  4) 위층 칸이면 바로 아래 상자가 칸에 바르게 놓여 멈춰 있어야 한다
        var support = index >= 4 ? boxes[index - 4] : null;
        if (index >= 4 && (support == null || !IsSettledAt(index - 4, support))) { return false; }

        //  5) 아래로 가장 먼저 닿는 표면이 컨테이너(아래층) 또는 등록된 아래 상자(위층)이고, 위를 보는 면이어야 한다
        //     들고 있는 상자 자신과 흡착 로봇은 건너뛴다
        foreach (var hit in Physics.RaycastAll(
                    box.bounds.center, Vector3.down, 
                    Mathf.Max(.1f, box.bounds.center.y - centre.y + parcelSize.z), ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
        {
            if (hit.collider.attachedRigidbody == body || hit.collider.transform.IsChildOf(loader.transform)) { continue; }
            bool valid = index < 4 ? hit.collider.transform.IsChildOf(transform) : hit.collider.attachedRigidbody == support.attachedRigidbody;
            return valid && Vector3.Dot(hit.normal, Vector3.up) > .9f;
        }
        return false;
    }


    //  ============================================================
    //  상자 확인
    //  ============================================================
    // 받는 크기인지. Collider 크기 × Scale이 parcelSize와 1 mm 안쪽으로 같아야 true.
    /// [호출] BoxAttacher (흡착 직전), TryNext(), 반복 적재 시험
    public bool IsCorrectSize(BoxCollider box)
    {
        if (box == null) { return false; }
        Vector3 size = Vector3.Scale(box.size, box.transform.lossyScale);
        return (size - parcelSize).sqrMagnitude < .000001f;
    }

    // 5 × 6 면(로컬 Z)이 위나 아래를 보는지. 컨테이너 위쪽과 약 10° 안쪽이면 true.
    /// [호출] TryNext() · Register() · ReadyToRelease() · IsSettledAt(), TotoPlayTest (에디터 도구)
    public bool Upright(BoxCollider box) => Mathf.Abs(Vector3.Dot(box.transform.forward.normalized, transform.up)) > .985f;

    // index 칸에 상자가 바르게 놓여 멈춰 있는지 (바로 서 있음 · 칸 자리 · 멈춤).
    /// [호출] BoxAttacher (흡착을 놓은 뒤 상자가 멈출 때까지 최대 5초 기다림), ReadyToRelease() (위층 칸의 아래 상자)
    public bool IsSettledAt(int index, BoxCollider box) => box != null && index >= 0 && index < Capacity && Upright(box) && NearSlot(box, index) && Settled(box);

    // 상자가 index 칸 자리에 있는지. 수평 positionTolerance, 높이 1.2 cm 안쪽 (컨테이너 축 기준).
    bool NearSlot(BoxCollider box, int index)
    {
        var delta = transform.InverseTransformVector(box.bounds.center - slots[index].position);
        return Mathf.Abs(delta.x) <= positionTolerance && Mathf.Abs(delta.z) <= positionTolerance && Mathf.Abs(delta.y) <= .012f;
    }

    // 상자가 멈춰 있는지. 속도 0.04 m/s · 회전 속도 0.3 rad/s 미만 (Rigidbody가 없으면 true).
    static bool Settled(BoxCollider box) => box.attachedRigidbody == null || (box.attachedRigidbody.linearVelocity.magnitude < .04f && box.attachedRigidbody.angularVelocity.magnitude < .3f);


    //  ============================================================
    //  반복 적재 시험 (Inspector 버튼 · 에디터 도구가 부름)
    //  ============================================================
    // 흡착 로봇에 이 컨테이너 · 칸을 넘기고, 다음 상자 하나를 넣게 한다.
    /// [호출] Inspector 버튼 "다음 박스 5×6면 흡착 → 2×2×2 적재" (ToteBaseEditor), 반복 적재 시험, TotoPlayTest
    public void StackNextBox()
    {
        if (loader == null) { Status = "흡착 로봇을 연결하세요."; return; }
        loader.tote = this;
        loader.container = transform;
        loader.slots = slots;
        loader.LoadNextBox();
    }

    // 반복 적재 시험을 시작한다. 지금 흡착 로봇에 지정된 상자부터 8칸이 찰 때까지 연속으로 넣는다.
    /// [호출] Inspector 버튼 "8개 연속 적재 시험" (ToteBaseEditor), SuctionLoadingSetup
    /// 실행 중이고, 흡착 로봇이 쉬고 있고(든 상자 없음), 빈 칸이 있을 때만 시작한다. 흡착 로봇의 Target Box가 받는 크기여야 한다.
    public void StartAutoStack()
    {
        if (!Application.isPlaying || AutoStacking || loader == null || loader.Busy || loader.Attached || Count >= Capacity) { return; }
        if (loader.targetBox == null || !IsCorrectSize(loader.targetBox))
            { Status = "집기 위치의 5×6×7cm 박스를 지정하세요."; return; }
        autoRoutine = StartCoroutine(AutoStack());
    }

    // 반복 적재 시험을 멈춘다. 흡착 로봇도 멈추고, 든 상자는 흡착한 채로 둔다.
    /// [호출] Inspector 버튼 "연속 적재 중단 (흡착 유지)" (ToteBaseEditor), SuctionLoadingSetup
    public void StopAutoStack()
    {
        if (autoRoutine != null) { StopCoroutine(autoRoutine); }
        autoRoutine = null;
        if (loader != null) { loader.StopLoading(); }
        if (testTemplate != null) { Destroy(testTemplate); }
        testTemplate = null;
        Status = loader != null && loader.Attached ? "반복 중단 · 흡착 유지" : "반복 중단";
    }

    // 반복 적재 시험 코루틴. 한 상자를 넣을 때마다 성공을 확인하고, 첫 상자 자리에 다음 시험 상자를 만든다.
    /// 다음 상자는 앞 상자를 칸에 놓고 흡착 도구가 물러난 뒤에만 만든다.
    /// [종료] 8칸이 차거나, 한 번이라도 실패하거나, 시험 상자를 만들 수 없으면 끝낸다.
    IEnumerator AutoStack()
    {
        yield return null;

        //  1) 첫 상자의 자리 · 바닥 높이 · 회전을 기억하고, Prefab이 없으면 첫 상자를 복제 원본으로 숨겨 둔다
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
            //  2) 한 상자 넣기 → 끝날 때까지 대기 → 한 칸 늘었는지 · 흡착을 놓았는지 확인
            int before = Count;
            Status = "자동 적재 · " + (before + 1) + "/8";
            StackNextBox();
            yield return null;
            while (loader.Busy) { yield return null; }
            if (Count != before + 1 || !loader.LastLoadSucceeded || loader.Attached)
                { Status = "자동 적재 중단 · " + loader.Status; break; }
            if (Count >= Capacity) 
                { Status = "8/8 적재 완료"; break; }
            yield return new WaitForSeconds(nextBoxDelay);

            //  3) 다음 시험 상자를 만든다. 방향은 따로 시험해 본 3가지 중 하나 (로컬 ±Z는 늘 5 × 6 면)
            //     X = 5 × 6 면이 월드 X 방향, Y = 위 · 아래, Z = 월드 Z 방향. 무작위가 꺼져 있으면 첫 상자 방향 그대로
            var prefab = testParcelPrefab != null ? testParcelPrefab : testTemplate;
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

            //  4) Collider · Rigidbody · 크기 확인. 맞지 않으면 지우고 끝낸다
            var box = next.GetComponent<BoxCollider>();
            var body = box != null ? box.attachedRigidbody : null;
            if (box == null || body == null || !IsCorrectSize(box))
                { Destroy(next); Status = "자동 적재 중단 · 생성 프리팹의 Collider/Rigidbody/크기 확인"; break; }

            //  5) 바닥을 첫 상자 바닥 높이에 맞춰 첫 상자 자리에 놓고, 흡착 로봇의 다음 상자로 지정한다
            next.transform.position += new Vector3(pickup.x - box.bounds.center.x, beltTop + box.bounds.extents.y + .001f - box.bounds.center.y, pickup.z - box.bounds.center.z);
            body.position = next.transform.position;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            loader.targetBox = box;
            yield return new WaitForSeconds(.3f);
        }

        //  6) 복제 원본을 지우고 끝낸다
        if (testTemplate != null) { Destroy(testTemplate); }
        testTemplate = null;
        autoRoutine = null;
    }

    // 꺼질 때 반복 적재 시험을 멈추고 복제 원본을 지운다.
    void OnDisable()
    {
        if (autoRoutine != null) { StopCoroutine(autoRoutine); }
        autoRoutine = null;
        if (testTemplate != null) { Destroy(testTemplate); }
        testTemplate = null;
    }


    //  ============================================================
    //  표시
    //  ============================================================
    // Scene 뷰에서 선택하면 칸을 그린다 (하늘색 = 아래층, 노랑 = 위층). 5 × 6 면이 위를 본 상자 크기로 그린다.
    void OnDrawGizmosSelected()
    {
        if (slots == null) { return; }
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
            {
                Gizmos.color = i < 4 ? Color.cyan : Color.yellow;
                Gizmos.matrix = Matrix4x4.TRS(slots[i].position, transform.rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(parcelSize.x, parcelSize.z, parcelSize.y));
            }
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
