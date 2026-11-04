using System.Collections;
using UnityEngine;

//  ============================================================
//  BoxPusher
//  분류 구간의 분기 지점에서, 목적지가 담당(target)인 상자를 옆 벨트나 NG 쪽으로 밀어내는 푸셔.
//      - 분기 지점마다 하나씩 둔다 (NG, A, C). 목적지가 B인 상자는 아무도 밀지 않아 그대로 직진한다.
//      - 상자가 분기 지점에 오면 ConveyorLine이 TryPush()를 부른다. 담당이면 구간에서 빼고(TakeAway) 밀기 시작.
//      - 판과 상자를 스크립트로 함께 옮긴다. 벨트(ConveyorLine)와 같은 방식이라 상자는 kinematic 그대로다.
//      - 다 밀면 toLine.Feed(box)로 옆 벨트에 넘기거나(A · C), toTote.Add(box)로 NG 토트에 넣고(NG), 판을 원위치로 되돌린다.
//      - A · C 푸셔는 같은 분기 지점의 양쪽에 있다. 밀기 전에 반대쪽 판을 벨트 아래로 내리고, 상자가 지나간 뒤 다시 올린다.
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
//      * To Line과 To Tote가 모두 비어 있으면 밀어낸 상자를 지운다 (다음 구간 없는 ConveyorLine 출구와 같음).
//      * 반대쪽 판: 박봉현님 10/30 BoxPusher는 밀기 동안 반대쪽 판 오브젝트를 껐다(SetActive). 여기서는 판만 벨트 아래로 내린다.
//        오브젝트를 끄면 그 푸셔의 BoxPusher도 꺼져 코루틴을 시작할 수 없기 때문이다.
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

    [Tooltip("(선택) 밀어낼 상자를 넣을 NG 토트 (NG 푸셔 → NgTote). To Line이 비어 있을 때 쓴다")]
    [SerializeField] NgTote toTote;
    [Tooltip("월드 좌표 밀기 방향. 0이면 기존 Transform 앞쪽 사용. 판 모델 회전과 별도로 설정.")]
    [SerializeField] Vector3 worldPushDirection;

    //  ---------------- 동작 ----------------
    [Header("동작")]
    [Tooltip("상자를 미는 거리 m. 분기 지점에서 받을 벨트 입구까지 (default: 0.25)")]
    [Min(0.01f)][SerializeField] float pushDistance = 0.8f;

    [Tooltip("판이 끝까지 나가는 시간(초). 돌아올 때도 같은 속도 (default: 0.6)")]
    [Min(0.05f)][SerializeField] float pushDuration = 0.6f;

    [Tooltip("받을 벨트가 차 있을 때 상자를 분기 지점에 세워 두는 최대 시간(초). 넘으면 \"분류 실패\"로 지나간다 (default: 10)")]
    [Min(0f)][SerializeField] float waitLimit = 10f;

    //  ---------------- 반대쪽 판 ----------------    (FR-09)
    [Header("반대쪽 판")]
    [Tooltip("(선택) 같은 분기 지점의 반대편 푸셔 (A 푸셔 ↔ C 푸셔). 밀기 동안 그 판을 벨트 아래로 내려 상자가 지나갈 길을 비운다. NG 푸셔는 비운다")]
    [SerializeField] BoxPusher oppositePusher;

    [Tooltip("반대쪽 판을 내리는 깊이 m. 판 높이보다 크게 두어 판 윗면이 벨트 면 아래로 내려가게 한다 (default: 0.15)")]
    [Min(0f)][SerializeField] float lowerDepth = 0.15f;

    [Tooltip("반대쪽 판을 내리거나 올리는 시간(초) (default: 0.2)")]
    [Min(0.01f)][SerializeField] float lowerDuration = 0.2f;

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

    /// 반대쪽 푸셔가 이 판을 벨트 아래로 내려 둔 상태. 이 동안은 밀지 않는다 (ConveyorLine은 상자를 분기 지점에 세워 둔다)
    public bool IsLowered { get; private set; }

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
            Vector3 direction = worldPushDirection.sqrMagnitude > .000001f ? worldPushDirection : transform.forward;
            direction.y = 0f;
            return direction.normalized;
        }
    }

    Vector3 raisedPosition;                                 //  내리기 전 판 위치 (다시 올릴 자리)


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

        if (oppositePusher == this)
        { Debug.LogWarning($"[Pusher] {name}: Opposite Pusher에 자기 자신이 들어 있습니다. 반대편 푸셔를 넣으세요.", this); oppositePusher = null; }

        if (oppositePusher == null && (target == Destination.A || target == Destination.C))
        { oppositePusher = FindOpposite(); }
    }

    // 같은 분기 지점(1 cm 안)을 쓰는 반대편 푸셔를 찾는다 (A ↔ C). 없으면 null.
    /// [호출] Start() (Opposite Pusher 칸이 비어 있을 때)
    BoxPusher FindOpposite()
    {
        if (divertPoint == null) { return null; }

        Destination other = target == Destination.A ? Destination.C : Destination.A;
        foreach (BoxPusher pusher in FindObjectsByType<BoxPusher>(FindObjectsSortMode.None))
        {
            if (pusher == this || pusher.target != other || pusher.divertPoint == null) { continue; }
            if (Vector3.Distance(pusher.divertPoint.position, divertPoint.position) > 0.01f) { continue; }

            Debug.Log($"[Pusher] {name}: 반대편 푸셔 {pusher.name}를 찾았습니다 (같은 분기 지점).", this);
            return pusher;
        }
        return null;
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
    /// 이 판이 내려가 있거나 반대쪽 푸셔가 밀고 있어도 false. 반대쪽 판이 올라오면 ConveyorLine이 다시 부른다.
    public bool TryPush(ConveyorLine line, BoxInfo box)
    {
        if (IsPushing || IsLowered || !WantsPushed(box)) { return false; }
        if (oppositePusher != null && oppositePusher.IsPushing) { return false; }
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
    /// 반대쪽 판은 밀기 전에 내리고, 판이 돌아온 뒤 상자가 그 자리를 벗어나면 올린다. 올릴 때까지 IsPushing은 true다.
    /// box가 null이면 판만 움직인다 (테스트).
    IEnumerator PushRoutine(BoxInfo box)
    {
        IsPushing = true;

        //  0) 반대쪽 판을 벨트 아래로 내린다 (상자가 지나갈 길)
        if (oppositePusher != null)
        { yield return oppositePusher.LowerRoutine(lowerDepth, lowerDuration); }

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

        //  2) 받을 곳에 넘긴다.
        ///  벨트: 입구가 차 있으면 빌 때까지 기다린다 (TryPush()가 확인했으므로 보통 바로 넘어간다)
        ///  토트: 다음 칸에 놓고 집계한다 (NgTote.Add -> CellController.Finish)
        if (box != null)
        {
            PushedCount++;
            if (toLine != null)
            {
                Debug.Log($"[Pusher] {name}: {box.boxId} 밀어냄 → {toLine.name} | 목적지 {box.destination}", this);
                while (box != null && !toLine.Feed(box)) { yield return null; }
            }
            else if (toTote != null)
            {
                Debug.Log($"[Pusher] {name}: {box.boxId} 밀어냄 → {toTote.name} | 목적지 {box.destination} · 사유 {box.reason}", this);
                toTote.Add(box);
            }
            else
            {
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

        //  4) 상자가 반대쪽 판 자리를 벗어나면 판을 다시 올린다
        if (oppositePusher != null)
        {
            while (oppositePusher.Blocks(box))
            { yield return new WaitForFixedUpdate(); }
            yield return oppositePusher.RaiseRoutine(lowerDuration);
        }
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
    //  반대쪽 판 (반대편 푸셔가 부른다)
    //  ============================================================
    // 판을 depth만큼 벨트 아래로 내린다. 이미 내려가 있으면 그대로 둔다.
    /// [호출] 반대편 BoxPusher.PushRoutine() 0단계
    public IEnumerator LowerRoutine(float depth, float duration)
    {
        if (IsLowered) { yield break; }

        IsLowered = true;
        raisedPosition = Head.position;
        yield return MoveHead(raisedPosition + Vector3.down * depth, depth / duration);
    }

    // 내려 둔 판을 원래 높이로 올린다.
    /// [호출] 반대편 BoxPusher.PushRoutine() 4단계
    public IEnumerator RaiseRoutine(float duration)
    {
        if (!IsLowered) { yield break; }

        float depth = Vector3.Distance(Head.position, raisedPosition);
        yield return MoveHead(raisedPosition, depth / duration);
        IsLowered = false;
    }

    // 판을 올렸을 때 상자와 겹치는지. 내려 둔 판의 경계 상자(Bounds)를 올린 자리로 옮겨 잰다.
    /// 상자가 없거나(지워짐) 판 렌더러 · 상자 Collider가 없으면 겹치지 않는 것으로 본다.
    public bool Blocks(BoxInfo box)
    {
        if (!IsLowered || box == null) { return false; }

        Renderer headRenderer = Head.GetComponentInChildren<Renderer>();
        Collider boxCollider = box.GetComponent<Collider>();
        if (headRenderer == null || boxCollider == null) { return false; }

        Bounds raised = headRenderer.bounds;
        raised.center += raisedPosition - Head.position;
        return raised.Intersects(boxCollider.bounds);
    }

    // 판을 goal까지 speed(m/s)로 옮긴다 (FixedUpdate마다).
    IEnumerator MoveHead(Vector3 goal, float speed)
    {
        Transform head = Head;
        while (Vector3.Distance(head.position, goal) > 0.0005f)
        {
            yield return new WaitForFixedUpdate();
            head.position = Vector3.MoveTowards(head.position, goal, speed * Time.fixedDeltaTime);
        }
        head.position = goal;
    }


    //  ============================================================
    //  테스트 (Play 중에만)
    //  ============================================================
    // [테스트] BoxPusher ⋮ 메뉴 → "테스트/판만 밀어 보기" (상자 없이 판이 나갔다 돌아오는지)
    [ContextMenu("테스트/판만 밀어 보기")]
    void TestStroke()
    {
        if (!Application.isPlaying || IsPushing || IsLowered) { return; }
        if (oppositePusher != null && oppositePusher.IsPushing) { return; }
        StartCoroutine(PushRoutine(null));
    }

    // 테스트 패널 (showTestButtons가 켜져 있을 때만)
    void OnGUI()
    {
        if (!showTestButtons) { return; }

        GUILayout.BeginArea(new Rect(guiPosition.x, guiPosition.y, 240, 70), GUI.skin.box);
        GUILayout.Label($"[푸셔 {target}] 밀어낸 수 {PushedCount} | {(IsPushing ? "동작 중" : IsLowered ? "판 내림" : "대기")}");
        GUI.enabled = !IsPushing && !IsLowered;
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
