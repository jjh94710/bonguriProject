using System.Collections.Generic;
using UnityEngine;

//  ============================================================
//  NgTote
//  NG 토트 (예외 레인). NG 푸셔가 밀어낸 예외 상자를 칸에 쌓고, Finish()로 집계한다.
//      - 다음 상자를 놓을 자리(열 × 줄 × 층)를 스크립트로 계산한다 (NextSlot).
//      - 적재 수가 경고 기준(CellSettings.ngWarnCount, 5개)을 넘으면 "NG 토트 정리 필요" 경고를 한 번 낸다.
//      - 정리(Clear)하면 상자를 지우고, 경고를 다시 낼 수 있게 된다.
//
//  [사용] NG 푸셔가 미는 자리 끝에 토트 오브젝트를 두고 붙인다. NG 푸셔(BoxPusher)의 To Tote 칸에 넣는다.
//         NG 푸셔가 NG 레인(ConveyorLine)으로 미는 씬은 From Line 칸에 그 레인을 넣는다. 레인 끝에 선 상자를 이 토트로 옮겨 담는다.
//         이 오브젝트의 위치 = 바닥 가운데, 앞쪽(+Z) = 줄 방향, 오른쪽(+X) = 열 방향.
//         선택하면 Scene 뷰에 칸이 하늘색 상자로 보인다 (다음 칸은 노랑).
//  [!] 쌓인 상자는 kinematic 그대로 둔다 (밀려 떨어지지 않게).
//  [!] Capacity는 경고 기준보다 넉넉하게 (default: 12) 둔다. 가득 찬 뒤에도 경고만 하고 받는다 (받지 않으면 푸셔가 든 상자를 놓을 곳이 없다).
//
//  [설계서와의 차이]
//      * ToteBase를 물려받지 않는다. 터미널 컨테이너는 박봉현 버전 ToteBase(흡착 적재 전용)를 쓰므로, NG 토트는 칸 계산을 직접 갖는다 (11/03 팀 협의).
//      * columns(열 수) 칸을 추가했다. 줄 수는 perLayer ÷ columns (올림)이다.
//      * 정리를 ⋮ 메뉴 "테스트/NG 토트 정리 (Clear)"로 두었다 (설계서: 테스트 버튼).
//      * fromLine 칸을 추가했다. 메인 씬은 NG 푸셔가 NG 레인으로 밀기 때문에, 레인 끝 상자를 로봇 없이 바로 수거한다.
//  ============================================================
[DisallowMultipleComponent]
public class NgTote : MonoBehaviour
{
    //  ---------------- 연결 ----------------    (FR-09)
    [Header("연결")]
    [Tooltip("(선택) CellController. 경고 알림 · 설정 읽기 · Finish 호출. 비우면 Console 로그만 남긴다")]
    [SerializeField] CellController cell;

    [Tooltip("(선택) NG 레인 구간. 끝 정지 위치에 선 상자를 이 토트로 옮겨 담는다. 비우면 NG 푸셔가 직접 넣는 상자만 받는다")]
    [SerializeField] ConveyorLine fromLine;

    //  ---------------- 칸 ----------------
    [Header("칸")]
    [Tooltip("최대 적재 수. 넘어도 받지만 경고한다 (default: 12)")]
    [Min(1)][SerializeField] int capacity = 12;

    [Tooltip("한 층의 열 수 (오른쪽 +X 방향) (default: 3)")]
    [Min(1)][SerializeField] int columns = 3;

    [Tooltip("한 층에 놓는 상자 수. 줄 수 = 이 값 ÷ 열 수 (올림) (default: 9)")]
    [Min(1)][SerializeField] int perLayer = 9;

    [Tooltip("칸 간격 m. 상자의 가장 긴 변보다 넉넉하게 (default: 0.09)")]
    [Min(0.01f)][SerializeField] float slotPitch = 0.09f;

    [Tooltip("바닥 윗면 높이 m (이 오브젝트 위치 기준) (default: 0)")]
    [SerializeField] float floorTop = 0f;

    [Tooltip("층 높이 m. 상자의 가장 긴 변 + 여유 (default: 0.075)")]
    [Min(0.01f)][SerializeField] float layerHeight = 0.075f;


    //  ============================================================
    //  상태 (푸셔 · 대시보드가 읽는 값)
    //  ============================================================
    /// 쌓인 상자 (놓인 순서)
    readonly List<BoxInfo> boxes = new List<BoxInfo>();

    /// 적재 수 (지워진 상자는 빼고 센다)
    public int Count
    {
        get
        {
            boxes.RemoveAll(box => box == null);
            return boxes.Count;
        }
    }

    /// 최대 적재 수, 가득 찼는지
    public int Capacity => capacity;
    public bool IsFull => Count >= Capacity;

    /// 경고를 이미 냈는지. Clear()하면 false로 돌아간다
    public bool Warned { get; private set; }

    /// 경고 기준 = CellSettings.ngWarnCount (실행 중 설정이 없으면 5)
    int WarnCount => cell != null && cell.Settings != null ? cell.Settings.ngWarnCount : 5;

    /// 줄 수 (perLayer ÷ columns 올림)
    int Rows => Mathf.Max(1, Mathf.CeilToInt(perLayer / (float)Mathf.Max(1, columns)));


    //  ============================================================
    //  칸 계산 · 적재
    //  ============================================================
    // 다음 상자를 놓을 월드 위치 (상자 바닥 가운데가 닿을 자리).
    public Vector3 NextSlot()
    {
        return SlotPosition(Count);
    }

    // index번째 칸의 월드 위치 (상자 바닥 가운데). 0번 = 1층 왼쪽 앞, 열 → 줄 → 층 순서.
    public Vector3 SlotPosition(int index)
    {
        int layer = index / Mathf.Max(1, perLayer);
        int inLayer = index % Mathf.Max(1, perLayer);
        int column = inLayer % Mathf.Max(1, columns);
        int row = inLayer / Mathf.Max(1, columns);

        //  가운데 기준으로 펼친다 (3열이면 −1, 0, +1칸)
        Vector3 local = new Vector3(
            (column - (columns - 1) * 0.5f) * slotPitch,
            floorTop + layer * layerHeight,
            (row - (Rows - 1) * 0.5f) * slotPitch);
        return transform.position + transform.rotation * local;
    }

    // NG 레인 끝에 선 상자를 토트로 옮겨 담는다 (로봇 없이 바로 수거, 한 프레임에 1개).
    /// fromLine 칸이 비어 있으면 아무것도 하지 않는다.
    void Update()
    {
        if (fromLine == null || !fromLine.HasBoxAtStop) { return; }

        BoxInfo box = fromLine.CurrentBox;
        fromLine.TakeAway(box);
        Add(box);
    }

    // 상자를 다음 칸에 놓고 등록한다 → 경고 확인 → cell.Finish(box).
    /// [호출] BoxPusher (NG 상자를 밀어낸 뒤), Update() (NG 레인 끝 수거)
    /// 상자는 kinematic으로 두고 이 오브젝트의 자식으로 옮긴다. 방향은 그대로 둔다.
    public void Add(BoxInfo box)
    {
        if (box == null) { return; }
        if (boxes.Contains(box)) { return; }

        if (IsFull)
        { Debug.LogWarning($"[Tote] {name}: 가득 찬 상태에서 {box.boxId}를 받았습니다 ({Count}/{Capacity}).", this); }

        //  1) 다음 칸에 놓는다 (바닥이 칸 높이에 닿게)
        Vector3 position = NextSlot() + Vector3.up * ConveyorLine.HalfSize(box, Vector3.up);
        Rigidbody body = box.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.position = position;
        }
        box.transform.position = position;

        //  2) 자식으로 등록한다
        box.transform.SetParent(transform, true);
        boxes.Add(box);
        Debug.Log($"[Tote] {name}: {box.boxId} NG 적재 {Count}개 | 사유 {box.reason}", this);

        //  3) 경고 기준을 넘으면 경고 1번 → 집계
        if (!Warned && Count > WarnCount)
        {
            Warned = true;
            Warn($"NG 토트 정리 필요 ({Count}개)", box);
        }
        if (cell != null) { cell.Finish(box); }
    }

    // NG 토트 정리. 쌓인 상자를 모두 지우고 경고를 초기화한다.
    /// 지우기 전에 자식에서 빼서, 같은 프레임 안에 Count가 0이 되게 한다.
    /// [!] 상자 오브젝트 전체(gameObject)를 지운다. 컴포넌트를 넘기면 그 컴포넌트만 지워진다.
    public void Clear()
    {
        foreach (BoxInfo box in boxes)
        {
            if (box == null) { continue; }
            box.transform.SetParent(null, true);
            Destroy(box.gameObject);
        }
        boxes.Clear();
        Warned = false;
        Debug.Log($"[Tote] {name}: NG 토트 정리", this);
    }

    // 경고를 알린다. CellController가 없으면 Console 경고만 남긴다.
    void Warn(string type, BoxInfo box)
    {
        if (cell != null) { cell.RaiseAlarm(type, box.boxId); }
        else { Debug.LogWarning($"[Tote] {name}: {type} | {box.boxId}", this); }
    }


    //  ============================================================
    //  테스트 · 표시
    //  ============================================================
    [ContextMenu("테스트/NG 토트 정리 (Clear)")]
    void TestClear()
    {
        if (Application.isPlaying) { Clear(); }
    }

    // Scene 뷰에서 선택하면 칸을 그린다 (하늘색 = 칸, 노랑 = 다음 칸).
    void OnDrawGizmosSelected()
    {
        int shown = Mathf.Max(capacity, perLayer);
        Vector3 size = new Vector3(slotPitch * 0.9f, layerHeight * 0.9f, slotPitch * 0.9f);
        int next = Application.isPlaying ? Count : 0;

        for (int i = 0; i < shown; i++)
        {
            Gizmos.color = i == next ? Color.yellow : new Color(0.3f, 0.8f, 1f, 0.6f);
            Vector3 centre = SlotPosition(i) + transform.up * (layerHeight * 0.45f);
            Gizmos.matrix = Matrix4x4.TRS(centre, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
}
