using System.Collections;
using UnityEngine;

//  ============================================================
//  BoxPusher
//  분류 구간의 분기 지점에서, 목적지가 담당(target)인 상자를 옆 벨트나 NG 쪽으로 밀어내는 푸셔.
//      - 분기 지점마다 하나씩 둔다 (NG, A, C). 목적지가 B인 상자는 아무도 밀지 않아 그대로 직진한다.
//      - 상자가 분기 지점에 오면 ConveyorLine이 TryPush()를 부른다. 담당이면 구간에서 빼고(TakeAway) 밀기 시작.
//      - 판과 상자를 스크립트로 함께 옮긴다. 벨트(ConveyorLine)와 같은 방식이라 상자는 kinematic 그대로다.
//      - 다 밀면 toLine.Feed(box)로 옆 벨트에 넘기고, 판을 원위치로 되돌린다.
//
//  [사용] 빈 오브젝트 "Pusher_A" 등에 붙이고, 판(Head)과 분기 지점(DivertPoint)을 자식으로 둔다.
//         이 오브젝트의 앞쪽(파란 화살표, +Z)이 미는 방향이다.
//         분류 구간 ConveyorLine의 Pushers 칸에 상자가 지나가는 순서대로 넣는다 (NG → A → C).
//  [!] 판은 충돌로 밀지 않는다. 판에 Collider가 있어도 상자 이동에는 영향이 없다.
//
//  [설계서와의 차이]
//      * 미는 방향은 이 오브젝트의 앞쪽(+Z)으로 정한다 (설계서에 칸 없음).
//      * pushDuration은 판이 끝까지 나가는 시간이다. 돌아올 때도 같은 속도로 온다.
//      * 받을 벨트 입구가 차 있으면 밀지 않는다. ConveyorLine이 상자를 분기 지점에 세워 두고, waitLimit을 넘기면 "분류 실패"로 지나가게 한다.
//      * 박봉현님 10/30 BoxPusher(실물 벨트 · 물리 밀기 · 수거함 모드)를 이 방식으로 바꿨다.
//  ============================================================
public class BoxPusher : MonoBehaviour
{
    //  ---------------- 담당 · 연결 ----------------    (FR-08, FR-09)
    [Header("담당 · 연결")]
    [Tooltip("담당 목적지. 이 목적지의 상자만 민다 (NG, A, C)")]
    [SerializeField] Destination target = Destination.NG;

    [Tooltip("분기 지점. 분류 구간 벨트 가운데, 상자를 세워 밀 자리에 둔다")]
    [SerializeField] Transform divertPoint;

    [Tooltip("판 (밀기 · 복귀 때 움직이는 막대). 비우면 이 오브젝트를 움직인다")]
    [SerializeField] Transform pusherHead;

    [Tooltip("(선택) 밀어낸 상자를 넘길 벨트 (LaneA, LaneC). 입구를 밀린 상자가 멈추는 자리(노란 선 끝)에 둔다")]
    [SerializeField] ConveyorLine toLine;

    ///// : TODO :
    //  [ToteBase 구현 후] toTote (ToteBase) 칸을 연다. NG 상자는 toTote.Add(box)로 넣는다.

    //  ---------------- 동작 ----------------
    [Header("동작")]
    [Tooltip("상자를 미는 거리 m. 분기 지점에서 받을 벨트 입구까지 (default: 0.25)")]
    [Min(0.01f)][SerializeField] float pushDistance = 0.25f;

    [Tooltip("판이 끝까지 나가는 시간(초). 돌아올 때도 같은 속도 (default: 0.6)")]
    [Min(0.05f)][SerializeField] float pushDuration = 0.6f;

    [Tooltip("받을 벨트가 차 있을 때 상자를 분기 지점에 세워 두는 최대 시간(초). 넘으면 \"분류 실패\"로 지나간다 (default: 10)")]
    [Min(0f)][SerializeField] float waitLimit = 10f;

    //  ---------------- 테스트 ----------------
    [Header("테스트")]
    [Tooltip("켜면 화면에 테스트 패널을 그린다")]
    [SerializeField] bool showTestButtons = false;

    [Tooltip("테스트 패널 위치 (x, y). 다른 패널과 겹치지 않게 바꾼다")]
    [SerializeField] Vector2 guiPosition = new Vector2(510, 10);


    //  ============================================================
    //  상태 (ConveyorLine · 대시보드가 읽는 값)
    //  ============================================================
    /// 밀기 동작 중 (판이 나갔다가 돌아올 때까지). 이 동안 ConveyorLine은 다음 상자를 분기 지점에 세운다
    public bool IsPushing { get; private set; }

    /// 밀어낸 상자 수 (대시보드 표시)
    public int PushedCount { get; private set; }

    /// 분기 지점 (ConveyorLine이 이 위치까지의 거리를 잰다)
    public Transform DivertPoint => divertPoint;

    /// 받을 벨트가 찼을 때 상자를 세워 두는 최대 시간(초)
    public float WaitLimit => waitLimit;

    /// 움직일 판 (비어 있으면 이 오브젝트)
    Transform Head => pusherHead != null ? pusherHead : transform;

    /// 미는 방향 (이 오브젝트의 앞쪽, 수평 단위 벡터)
    Vector3 PushDirection
    {
        get
        {
            Vector3 direction = transform.forward;
            direction.y = 0f;
            return direction.normalized;
        }
    }


    //  ============================================================
    //  시작
    //  ============================================================
    // 칸 확인. 빠진 칸이 있어도 멈추지 않고 경고만 남긴다.
    void Start()
    {
        if (divertPoint == null)
        { Debug.LogWarning($"[Pusher] {name}: Divert Point가 비어 있습니다. 상자를 세우지도 밀지도 않습니다.", this); }

        if (target == Destination.None || target == Destination.B)
        { Debug.LogWarning($"[Pusher] {name}: Target이 {target}입니다. B는 직진이라 푸셔가 필요 없습니다. NG · A · C 중 하나로 바꾸세요.", this); }

        if (toLine == null && target != Destination.NG)
        { Debug.LogWarning($"[Pusher] {name}: To Line이 비어 있습니다. 밀어낸 상자는 지워집니다.", this); }
    }


    //  ============================================================
    //  명령 (ConveyorLine이 부른다)
    //  ============================================================
    // 이 상자가 담당인지 (목적지 == target).
    public bool WantsPushed(BoxInfo box)
    {
        return box != null && box.destination == target;
    }

    // 분기 지점에 온 상자를 민다. 담당이 아니거나, 밀기 중이거나, 받을 벨트 입구가 차 있으면 false (상자는 그대로).
    /// [호출] ConveyorLine.CarryRoutine() (상자가 분기 지점에 왔을 때)
    /// 밀기로 하면 line.TakeAway(box)로 구간에서 빼고 PushRoutine()을 시작한다.
    public bool TryPush(ConveyorLine line, BoxInfo box)
    {
        if (IsPushing || !WantsPushed(box)) { return false; }
        if (toLine != null && !toLine.CanAccept) { return false; }

        line.TakeAway(box);
        StartCoroutine(PushRoutine(box));
        return true;
    }


    //  ============================================================
    //  밀기 (코루틴)
    //  ============================================================
    // 판을 내밀어 상자를 pushDistance만큼 옮기고, 받을 벨트에 넘긴 뒤 판을 되돌린다.
    /// 판이 상자 옆면에 닿은 뒤부터 상자가 같이 움직인다 (닿기 전 거리는 ContactGap()이 잰다).
    /// box가 null이면 판만 움직인다 (테스트).
    IEnumerator PushRoutine(BoxInfo box)
    {
        IsPushing = true;
        Transform head = Head;
        Vector3 direction = PushDirection;
        Vector3 home = head.position;

        float contact = box != null ? ContactGap(box, direction) : 0f;
        float travel = contact + pushDistance;
        float speed = travel / pushDuration;

        //  1) 판을 내민다. 판이 contact만큼 나간 뒤부터 상자도 같은 만큼 간다
        for (float moved = 0f; moved < travel;)
        {
            yield return new WaitForFixedUpdate();

            float step = Mathf.Min(speed * Time.fixedDeltaTime, travel - moved);
            moved += step;
            head.position += direction * step;

            float boxStep = Mathf.Clamp(moved - contact, 0f, step);
            if (box != null && boxStep > 0f) { ConveyorLine.MoveBox(box, direction * boxStep); }
        }

        //  2) 받을 벨트에 넘긴다. 입구가 차 있으면 빌 때까지 기다린다 (TryPush()가 확인했으므로 보통 바로 넘어간다)
        if (box != null)
        {
            PushedCount++;
            if (toLine != null)
            {
                Debug.Log($"[Pusher] {name}: {box.boxId} 밀어냄 → {toLine.name} | 목적지 {box.destination}", this);
                while (box != null && !toLine.Feed(box)) { yield return null; }
            }
            else
            {
                ///// : TODO :
                //  [ToteBase 구현 후] toTote.Add(box). 지금은 받을 곳이 없어 지운다 (다음 구간 없는 ConveyorLine 출구와 같음).
                Debug.Log($"[Pusher] {name}: {box.boxId} 밀어냄 (받을 곳 없음, 지움) | 목적지 {box.destination}", this);
                Destroy(box.gameObject);
            }
        }

        //  3) 판을 원위치로 되돌린다 (나갈 때와 같은 속도)
        while (Vector3.Distance(head.position, home) > 0.0005f)
        {
            yield return new WaitForFixedUpdate();
            head.position = Vector3.MoveTowards(head.position, home, speed * Time.fixedDeltaTime);
        }
        head.position = home;
        IsPushing = false;
    }

    // 판 앞면에서 상자 옆면까지 거리 m (미는 방향으로 잰다). 판이 이만큼 나가야 상자에 닿는다.
    /// 판 앞면은 판 렌더러의 경계 상자(Bounds)로 잰다. 렌더러가 없으면 판 중심을 앞면으로 본다.
    float ContactGap(BoxInfo box, Vector3 direction)
    {
        Transform head = Head;
        float headFront = Vector3.Dot(head.position, direction);

        Renderer headRenderer = head.GetComponentInChildren<Renderer>();
        if (headRenderer != null)
        {
            Bounds bounds = headRenderer.bounds;
            Vector3 reach = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            headFront = Vector3.Dot(bounds.center, direction) + Vector3.Dot(bounds.extents, reach);
        }

        float boxBack = Vector3.Dot(ConveyorLine.PositionOf(box), direction) - ConveyorLine.HalfSize(box, direction);
        return Mathf.Max(0f, boxBack - headFront);
    }


    //  ============================================================
    //  테스트 (Play 중에만)
    //  ============================================================
    // [테스트] BoxPusher ⋮ 메뉴 → "테스트/판만 밀어 보기" (상자 없이 판이 나갔다 돌아오는지)
    [ContextMenu("테스트/판만 밀어 보기")]
    void TestStroke()
    {
        if (!Application.isPlaying || IsPushing) { return; }
        StartCoroutine(PushRoutine(null));
    }

    // 테스트 패널 (showTestButtons가 켜져 있을 때만)
    void OnGUI()
    {
        if (!showTestButtons) { return; }

        GUILayout.BeginArea(new Rect(guiPosition.x, guiPosition.y, 240, 70), GUI.skin.box);
        GUILayout.Label($"[푸셔 {target}] 밀어낸 수 {PushedCount} | {(IsPushing ? "동작 중" : "대기")}");
        GUI.enabled = !IsPushing;
        if (GUILayout.Button("판만 밀어 보기")) { TestStroke(); }
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    // Scene 뷰에서 선택하면 분기 지점(주황 공)과 밀린 상자가 멈출 자리(노란 선 끝)를 그린다.
    void OnDrawGizmosSelected()
    {
        if (divertPoint == null) { return; }

        Vector3 end = divertPoint.position + PushDirection * pushDistance;
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawSphere(divertPoint.position, 0.01f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(divertPoint.position, end);
        Gizmos.DrawWireSphere(end, 0.01f);
    }
}