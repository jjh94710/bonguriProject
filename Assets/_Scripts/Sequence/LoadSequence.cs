using System.Collections;
using UnityEngine;

//  ============================================================
//  LoadSequence
//  터미널 끝 흡착 로봇의 공정 연결. 레인 끝에 선 상자를 봉현님 BoxAttacher로 컨테이너(ToteBase)에 넣고, Finish()로 집계한다.
//      - 터미널 A · B · C 흡착 로봇마다 하나씩 붙인다. 같은 스크립트를 쓰고 Lane · Terminal 칸만 다르다.
//      - 흡착 · 운반 · 놓기는 모두 BoxAttacher.LoadNextBox()가 한다. 이 스크립트는 언제 부를지와 결과 처리만 맡는다.
//      - 컨테이너가 가득 차면 들어 있는 상자를 지워 비운다 (자동 비우기, 출하 추상화).
//
//  [순서]  레인 끝 도착 → 물리 켜기 → BoxAttacher.targetBox 지정 → LoadNextBox() → 끝날 때까지 대기
//          → 컨테이너에 들어갔으면: 레인에서 빼기(TakeAway) → CellController.Finish() → 가득 차면 자동 비우기
//          → 못 넣었으면: "적재 실패" 경고. 벨트 위에 남았으면 작업자 확인을 기다리고, 든 채 끝났으면 이 로봇만 멈춘다.
//  [사용]  fr5_suction 루트(BoxAttacher · BackArmIK가 있는 오브젝트)에 붙인다.
//          BoxAttacher 칸(tcp · 흡착컵 · 컨테이너 · 슬롯 · 준비 자세)은 봉현님 도구 Tools/Bonguri/Configure Suction Loading으로 맞춘다.
//  [!] 레인 끝 정지 위치가 흡착 로봇이 집는 자리이다. 그 레인의 Auto Release는 끈다.
//  [!] 벨트 위 상자는 kinematic이라 BoxAttacher가 붙이지 않는다. LoadNextBox() 직전에 물리를 켠다.
//  [!] 봉현님 ToteBase는 5 × 6 × 7 cm 상자만 받는다 (공정 상자 ParcelBox_ProcessVariant와 같은 크기).
//
//  [설계서와의 차이]
//      * 흡착 · 칸 계산 · 놓기는 봉현님 BoxAttacher · ToteBase(2 × 2 × 2, 8칸)를 그대로 쓴다 (11/03 팀 원칙).
//        설계서: LoadSequence가 순서를 정하고 BoxAttacher는 붙이기 · 떼기만, 컨테이너는 3 × 3 · 10개.
//      * AgvContainer는 만들지 않았다. 오분류 경고 · 자동 비우기는 이 스크립트가 한다.
//      * 가득 참 기준은 ToteBase 칸 수(8)와 CellSettings.containerFull(10) 중 작은 값이다.
//      * 흡착 재시도는 하지 않는다 (BoxAttacher가 1번 시도한다).
//      * StopLoading()은 지금 옮기는 상자를 다 넣은 뒤 멈춘다. 중간에 끊으면 상자를 든 채 멈추기 때문.
//  ============================================================
[DisallowMultipleComponent]
public class LoadSequence : MonoBehaviour
{
    //  ---------------- 연결 ----------------    (FR-08, FR-15)
    [Header("연결")]
    [Tooltip("(선택) 흡착 로봇 BoxAttacher. 비우면 같은 오브젝트에서 찾는다")]
    [SerializeField] BoxAttacher attacher;

    [Tooltip("이 로봇이 맡는 터미널 레인 (끝에 정지 위치가 있는 ConveyorLine)")]
    [SerializeField] ConveyorLine lane;

    [Tooltip("담당 터미널 (A, B, C). 다른 목적지의 상자를 넣으면 \"오분류\" 경고")]
    [SerializeField] Destination terminal = Destination.B;

    [Tooltip("(선택) CellController. 집계(Finish) · 경고 · 설정 읽기. 비우면 Console 로그만 남긴다")]
    [SerializeField] CellController cell;

    //  ---------------- 동작 ----------------
    [Header("동작")]
    [Tooltip("물리를 켠 뒤 상자가 벨트 위에 자리 잡기를 기다리는 시간(초) (default: 0.2)")]
    [Min(0f)][SerializeField] float settleTime = 0.2f;

    [Tooltip("적재 1회 제한 시간(초). 넘으면 BoxAttacher를 멈추고 적재 실패로 본다 (default: 90)")]
    [Min(1f)][SerializeField] float loadTimeLimit = 90f;

    //  ---------------- 상태 ----------------
    [Header("상태 (보기 전용)")]
    [Tooltip("지금 상태. Play 중에 바뀐다")]
    [SerializeField] string status = "대기";


    //  ============================================================
    //  상태 (CellController · 대시보드가 읽는 값)
    //  ============================================================
    /// 감시 중인지 (StartLoading() 뒤, 끝나기 전까지)
    public bool IsLoading => loop != null;

    /// 적재 실패로 이 로봇만 멈췄는지. 작업자가 상자를 치운 뒤 StartLoading()으로 다시 시작한다
    public bool IsHalted { get; private set; }

    /// 지금 상태 문구
    public string Status => status;

    /// 이 로봇이 넣은 상자 수, 자동 비우기로 뺀 상자 수, 마지막 적재 1회 시간(초)
    public int LoadedCount { get; private set; }
    public int EmptiedCount { get; private set; }
    public float LastLoadSeconds { get; private set; }

    /// 가득 참 기준 = ToteBase 칸 수와 CellSettings.containerFull 중 작은 값
    int Full
    {
        get
        {
            int full = attacher.toto.Capacity;
            if (cell != null && cell.Settings != null) { full = Mathf.Min(full, cell.Settings.containerFull); }
            return full;
        }
    }

    Coroutine loop;             //  감시 루프 (LoadLoop)
    bool stopRequested;         //  StopLoading()을 받았는지 (지금 상자를 넣은 뒤 멈춘다)
    BoxInfo failedBox;          //  적재에 실패해 벨트 위에 남은 상자 (작업자 확인 대기, 다시 집지 않는다)


    //  ============================================================
    //  시작
    //  ============================================================
    // 빈 칸을 같은 오브젝트에서 찾는다.
    void Awake()
    {
        if (attacher == null) { attacher = GetComponent<BoxAttacher>(); }
    }


    //  ============================================================
    //  명령 (CellController · 테스트 메뉴가 부른다)
    //  ============================================================
    // 레인 감시를 시작한다. 레인 끝에 상자가 서면 스스로 컨테이너에 넣는다.
    /// [호출] CellController (운전 시작), 테스트 메뉴
    /// [주의] 필수 칸이 비어 있으면 시작하지 않는다. 멈추는 중에 다시 부르면 멈추지 않고 계속 돈다.
    public void StartLoading()
    {
        stopRequested = false;
        if (loop != null) { return; }
        if (!Check()) { return; }

        IsHalted = false;
        status = "감시 중";
        loop = StartCoroutine(LoadLoop());
        Debug.Log($"[Load] {name}: 감시 시작 | {lane.name} → {attacher.toto.name} (가득 참 {Full}개)", this);
    }

    // 감시를 멈춘다. 지금 옮기는 상자가 있으면 다 넣은 뒤 멈춘다.
    /// [호출] CellController (운전 끝, 알람 정지), 테스트 메뉴
    public void StopLoading()
    {
        if (loop == null || stopRequested) { return; }

        stopRequested = true;
        Debug.Log($"[Load] {name}: 감시 정지 요청 | 옮기는 상자가 있으면 넣은 뒤 멈춤", this);
    }

    // 적재에 실패해 벨트 위에 남은 상자를 다시 시도하게 한다 (원인을 고친 뒤 부른다).
    public void RetryFailed()
    {
        if (failedBox == null) { return; }

        Debug.Log($"[Load] {name}: {failedBox.boxId} 다시 시도", this);
        failedBox = null;
        if (loop != null) { status = "감시 중"; }
    }

    // 컨테이너를 비운다. 들어 있는 상자를 지운다 (집계는 이미 Finish()에서 끝났다).
    /// [!] Destroy는 프레임 끝에 일어난다. 비운 뒤 한 프레임 지나야 ToteBase.Count가 0이 된다.
    /// [!] 상자 오브젝트 전체(gameObject)를 지운다. 컴포넌트를 넘기면 그 컴포넌트만 지워진다.
    public void EmptyContainer()
    {
        if (attacher == null || attacher.toto == null) { return; }

        int count = 0;
        foreach (BoxCollider placed in attacher.toto.Boxes)
        {
            if (placed == null) { continue; }
            Destroy(placed.gameObject);
            count++;
        }
        EmptiedCount += count;
        Debug.Log($"[Load] {name}: {attacher.toto.name} 자동 비우기 {count}개 (누적 {EmptiedCount}개)", this);
    }


    //  ============================================================
    //  적재 흐름 (코루틴)
    //  ============================================================
    // 감시 루프: 레인 끝 상자를 하나씩 컨테이너에 넣는다. 멈춤 요청이나 이 로봇의 정지가 될 때까지 돈다.
    IEnumerator LoadLoop()
    {
        while (!stopRequested && !IsHalted)
        {
            //  1) 레인 끝에 새 상자가 서고, 로봇이 쉬고 있을 때까지 기다린다 (실패해 남은 상자는 다시 집지 않는다)
            BoxInfo box = lane.CurrentBox;
            if (box == null || box == failedBox || attacher.Busy || (attacher.armIK != null && attacher.armIK.Moving))
                { yield return null; continue; }

            //  2) 상자 1개
            yield return LoadOne(box);

            //  3) 가득 찼으면 비운다. 지운 상자가 빠지도록 한 프레임 기다린다
            if (attacher.toto.Count >= Full)
            {
                EmptyContainer();
                yield return null;
            }
        }

        loop = null;
        if (!IsHalted) { status = "정지"; }
        Debug.Log($"[Load] {name}: 감시 끝 | 적재 {LoadedCount}개 · 비움 {EmptiedCount}개", this);
    }

    // 상자 1개: 물리 켜기 → BoxAttacher.LoadNextBox() → 끝날 때까지 대기 → 결과 처리.
    /// [종료] 컨테이너에 들어갔으면 레인에서 빼고 Finish(). 못 넣었으면 Fail().
    /// 성공 여부는 ToteBase에 등록됐는지로 본다. BoxAttacher.LastLoadSucceeded는 팔이 위로 빠져나왔는지까지 포함한다.
    IEnumerator LoadOne(BoxInfo box)
    {
        float startTime = Time.time;
        BoxCollider boxCollider = box.GetComponent<BoxCollider>();
        box.state = BoxState.Loading;
        status = $"적재 중 ({box.boxId})";
        Debug.Log($"[Load] {name}: {box.boxId} 적재 시작 | 목적지 {box.destination}", this);

        //  1) 물리를 켜고 벨트 위에 자리 잡기를 기다린다 (BoxAttacher는 kinematic 상자를 붙이지 않는다)
        Rigidbody body = box.GetComponent<Rigidbody>();
        if (body != null) { body.isKinematic = false; }
        yield return new WaitForSeconds(settleTime);
        if (box == null) { yield break; }                           //  기다리는 사이 상자가 지워짐

        //  2) BoxAttacher에 상자를 넘기고 적재를 시작한다. 시작하지 못하면 바로 실패
        attacher.targetBox = boxCollider;
        attacher.LoadNextBox();
        if (!attacher.Busy)
        {
            Fail(box, attacher.Status);
            yield break;
        }

        //  3) 끝날 때까지 기다린다. 제한 시간을 넘으면 BoxAttacher를 멈춘다
        while (attacher.Busy)
        {
            if (Time.time - startTime > loadTimeLimit)
            {
                attacher.StopLoading();
                Debug.LogWarning($"[Load] {name}: {box.boxId} 적재 제한 시간 {loadTimeLimit:0}초 초과", this);
                break;
            }
            yield return null;
        }
        LastLoadSeconds = Time.time - startTime;
        if (box == null) { yield break; }                           //  적재 중에 상자가 지워짐

        //  4) 컨테이너에 등록되지 않았으면 실패
        if (System.Array.IndexOf(attacher.toto.Boxes, boxCollider) < 0)
        {
            Fail(box, attacher.Status);
            yield break;
        }

        //  5) 적재: 레인에서 빼고, 오분류를 확인하고, 집계한다
        lane.TakeAway(box);
        LoadedCount++;
        if (box.destination != terminal)
            { Warn($"오분류 (목적지 {box.destination} → 터미널 {terminal})", box); }

        Debug.Log($"[Load] {name}: {box.boxId} 적재 → {attacher.toto.name} {attacher.toto.Count}/{Full} | {LastLoadSeconds:F1}초", this);
        if (cell != null) { cell.Finish(box); }

        //  6) 상자는 넣었지만 팔이 위로 빠져나오지 못했으면 이 로봇만 멈춘다 (다음 상자 경로가 틀어진다)
        if (!attacher.LastLoadSucceeded)
        {
            IsHalted = true;
            status = $"팔 복귀 실패로 정지 ({attacher.Status})";
            Warn("적재 로봇 복귀 실패", box);
        }
        else { status = "감시 중"; }
    }

    // 적재 실패: 경고를 올리고, 상자가 어디 있는지에 따라 기다리거나 멈춘다.
    /// 든 채(Attached): 이 로봇만 멈춘다. 작업자가 상자를 지우면(흡착도 풀린다) StartLoading()으로 다시 시작한다.
    /// 벨트 위: 그 상자는 다시 집지 않는다. 작업자가 치우거나, 원인을 고친 뒤 RetryFailed()로 다시 시도한다.
    void Fail(BoxInfo box, string reason)
    {
        Debug.LogWarning($"[Load] {name}: {box.boxId} 적재 실패 | {reason}", this);
        if (attacher.Attached)
        {
            IsHalted = true;
            status = $"적재 실패 · 상자를 든 채 정지 ({box.boxId})";
        }
        else
        {
            failedBox = box;
            status = $"적재 실패 · 작업자 확인 대기 ({box.boxId})";
        }
        Warn("적재 실패", box);
    }

    // 경고를 알린다. CellController가 없으면 Console 경고만 남긴다.
    void Warn(string type, BoxInfo box)
    {
        if (cell != null) { cell.RaiseAlarm(type, box.boxId); }
        else { Debug.LogWarning($"[Load] {name}: {type} | {box.boxId}", this); }
    }


    //  ============================================================
    //  점검
    //  ============================================================
    // 필수 칸 확인. 빠진 칸이 있으면 이름을 알려 주고 false.
    bool Check()
    {
        string missing = attacher == null ? "Attacher (같은 오브젝트에 BoxAttacher가 없음)"
                       : attacher.toto == null ? "BoxAttacher의 Toto (ToteBase)"
                       : lane == null ? "Lane"
                       : null;
        if (missing != null)
        {
            Debug.LogWarning($"[Load] {name}: {missing} 칸이 비어 있어 감시를 시작하지 않습니다.", this);
            return false;
        }

        if (terminal == Destination.None || terminal == Destination.NG)
            { Debug.LogWarning($"[Load] {name}: Terminal이 {terminal}입니다. A · B · C 중 하나로 바꾸세요.", this); }
        return true;
    }


    //  ============================================================
    //  테스트 (LoadSequence ⋮ 메뉴, Play 중에만)
    //  ============================================================
    // Play 중인지 확인 (아니면 경고)
    bool Playing()
    {
        if (Application.isPlaying) { return true; }
        Debug.LogWarning($"[Load] {name}: Play 중에만 실행할 수 있습니다.", this);
        return false;
    }

    [ContextMenu("테스트/감시 시작")]
    void TestStart()
    {
        if (Playing()) { StartLoading(); }
    }

    [ContextMenu("테스트/감시 정지")]
    void TestStop()
    {
        if (Playing()) { StopLoading(); }
    }

    [ContextMenu("테스트/실패 상자 다시 시도")]
    void TestRetry()
    {
        if (Playing()) { RetryFailed(); }
    }

    [ContextMenu("테스트/컨테이너 비우기")]
    void TestEmpty()
    {
        if (Playing()) { EmptyContainer(); }
    }
}
