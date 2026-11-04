using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//  ============================================================
//  LoadSequence
//  터미널 흡착 로봇 1대의 이송 · 적재 담당. 레인 끝에 선 상자를 BoxAttacher로 수거함(ToteBase)에 넣고, CellController.Finish()로 집계한다.
//      - 터미널 A · B · C 흡착 로봇마다 하나씩 붙인다. 같은 스크립트를 쓰고 Lane · Terminal 칸만 다르다.
//      - 흡착 · IK · 슬롯 배치는 BoxAttacher(TryLoadBox)와 ToteBase가 한다. 이 스크립트는 언제 부를지와 결과 처리만 맡는다.
//      - CellController는 운전 시작 · 정지와 최종 집계만 맡는다 (StartLoading · StopLoading을 부른다).
//
//  [순서]  구간 사이 이송 (Lane → NextLine → … → 마지막 구간) → 마지막 구간 정지 위치에 도착
//          → 확인 (NG 상자 · Collider · 집기 범위 · 수거함 가득 참) → TryLoadBox() → 끝날 때까지 대기
//          → 들어갔으면: 레인에서 빼기(TakeAway) → CellController.Finish(box, terminal)
//          → 못 넣었으면: 상자가 정지 위치에 그대로면 1번 더. 그래도 안 되면 "적재 실패" 경고, 그 상자는 다시 집지 않는다.
//  [사용]  fr5_suction 루트(BoxAttacher가 있는 오브젝트)에 붙이고, CellController의 Load Sequences 칸에 넣는다.
//          메인 씬은 지훈님 도구 Tools/Bonguri/Integration/Connect Terminal Loading To Cell이 연결한다.
//  [!] Lane은 터미널 레인의 "첫" 구간이다. NextLine을 끝까지 따라간 마지막 구간(EndLine)의 정지 위치에서 집는다.
//  [!] 수거함이 가득 차도 자동으로 비우지 않는다. 경고를 내고, 작업자가 비울 때까지(⋮ "테스트/컨테이너 비우기") 기다린다.
//  [!] 적재에 실패한 상자는 작업자가 다시 시도(RetryFailed)하기 전까지 벨트 위에 그대로 둔다.
//  [!] Cell 칸이 비어 있으면 운전과 상관없이 혼자 돈다 (⋮ "테스트/감시 시작"으로 시험할 때).
//
//  [설계서와의 차이]
//      * 흡착 · 칸 계산 · 놓기는 봉현님 BoxAttacher · ToteBase(2 × 2 × 2, 8칸)를 그대로 쓴다 (11/03 팀 원칙).
//      * 자동 비우기(FR-08)는 하지 않는다. 가득 참 기준은 ToteBase 칸 수(8)이고, CellSettings.containerFull은 쓰지 않는다.
//      * 오분류 경고는 없다. Finish(box, terminal)로 실제 도착 터미널을 넘기면 CellController가 "분류 실패"로 센다.
//      * 터미널 적재 순서는 지훈님 CellController(c652e5b) 코드를 옮겨 온 것이다 (e8bacf5).
//  ============================================================
[DisallowMultipleComponent]
public class LoadSequence : MonoBehaviour
{
    //  ---------------- 연결 ----------------    (FR-08)
    [Header("연결")]
    [Tooltip("(선택) 흡착 로봇 BoxAttacher. 비우면 같은 오브젝트에서 찾는다")]
    [SerializeField] BoxAttacher attacher;

    [Tooltip("이 로봇이 맡는 터미널 레인의 첫 구간. NextLine을 따라간 마지막 구간의 정지 위치에서 집는다")]
    [SerializeField] ConveyorLine lane;

    [Tooltip("담당 터미널 (A, B, C)")]
    [SerializeField] Destination terminal = Destination.B;

    [Tooltip("CellController. 운전 상태 · 집계(Finish) · 경고. 비우면 혼자 돌고 Console 경고만 남긴다")]
    [SerializeField] CellController cell;

    //  ---------------- 동작 ----------------
    [Header("동작")]
    [Tooltip("로봇 루트에서 상자까지 허용 거리(m). 멀면 적재 자리가 아니라 이송 대기로 본다 (default: 1)")]
    [Min(.1f)][SerializeField] float maxPickupDistance = 1f;

    [Tooltip("적재 1회 제한 시간(초). 넘으면 BoxAttacher를 멈추고 실패로 본다 (default: 180)")]
    [Min(1f)][SerializeField] float loadTimeLimit = 180f;

    [Tooltip("켜면 수거함이 가득 찼을 때 쌓인 상자를 지우고 이어서 싣는다 (FR-08). 끄면 작업자가 비울 때까지 기다린다 (default: 꺼짐)")]
    [SerializeField] bool autoEmpty = false;

    //  ---------------- 상태 ----------------
    [Header("상태 (보기 전용)")]
    [Tooltip("지금 상태. Play 중에 바뀐다")]
    [SerializeField] string status = "대기";


    //  ============================================================
    //  상태 (CellController · 대시보드 · 에디터 도구가 읽는 값)
    //  ============================================================
    /// 연결 칸 (CellController.ValidateTerminalConfiguration()이 중복을 확인할 때 읽는다)
    public BoxAttacher Attacher => attacher;
    public ConveyorLine Lane => lane;

    /// 로봇이 집는 구간 = Lane에서 NextLine을 끝까지 따라간 구간. 순환하면 null
    public ConveyorLine EndLine => TerminalEnd(lane);

    public Destination Terminal => terminal;
    public CellController Cell => cell;

    /// 감시 중인지 (StartLoading() 뒤, 끝나기 전까지)
    public bool IsLoading => loop != null;

    /// 적재 실패 때 상자를 든 채였는지. RetryFailed()로 false가 된다
    /// [참고] 이 값으로 루프를 멈추지는 않는다. 든 채(Attached)면 LoadLoop()가 어차피 다음 상자를 집지 않는다.
    public bool IsHalted { get; private set; }

    /// 지금 상태 문구
    public string Status => status;

    /// 이 로봇이 넣은 상자 수, EmptyContainer()로 지운 상자 수, 마지막 적재 1회 시간(초, 확인부터 들어갈 때까지)
    public int LoadedCount { get; private set; }
    public int EmptiedCount { get; private set; }
    public float LastLoadSeconds { get; private set; }


    //  ============================================================
    //  안쪽 값
    //  ============================================================
    /// 알람으로 멈춘 상태인지 (Cell 칸이 비어 있으면 false)
    bool AlarmActive => cell != null && cell.IsAlarmActive;

    /// 계속 돌아도 되는지: 정지 요청이 없고, 운전 중(Cell이 비면 상관없음)이고, 알람이 아님
    bool Running => !stopRequested && (cell == null || cell.IsRunning) && !AlarmActive;

    Coroutine loop;                                                         //  감시 루프 (LoadLoop)
    bool stopRequested;                                                     //  StopLoading()을 받았는지
    readonly HashSet<BoxInfo> blockedLoads = new HashSet<BoxInfo>();        //  적재 실패 · 가득 참으로 막은 상자 (다시 집지 않는다)
    readonly HashSet<BoxInfo> waitingTransfers = new HashSet<BoxInfo>();    //  집기 범위 밖 정지 위치에서 이송을 기다리는 상자 (로그 1번만)


    //  ============================================================
    //  시작 · 끝
    //  ============================================================
    // 빈 칸을 같은 오브젝트에서 찾는다.
    void Awake()
    {
        if (attacher == null)
            { attacher = GetComponent<BoxAttacher>(); }
    }

    // 꺼지면 감시와 로봇을 멈춘다.
    void OnDisable()
    {
        StopLoading();
    }


    //  ============================================================
    //  명령 (CellController · 테스트 메뉴가 부른다)
    //  ============================================================
    // 레인 감시를 시작한다. 레인 끝에 상자가 서면 스스로 수거함에 넣는다.
    /// [호출] CellController (운전 시작), 테스트 메뉴
    /// [주의] 연결 확인(ValidateConfiguration)에 실패하면 경고만 남기고 시작하지 않는다.
    public void StartLoading()
    {
        if (loop != null) { return; }
        if (!ValidateConfiguration(out string error))
        {
            Debug.LogWarning("[Load] " + error, this);
            return;
        }

        stopRequested = false;
        IsHalted = false;
        status = "감시 중";
        loop = StartCoroutine(LoadLoop());
    }

    // 감시를 바로 멈춘다. 옮기는 중인 로봇이 있으면 그 자리에서 세우고, 그 상자는 다시 집지 않게 막는다.
    /// [호출] CellController (운전 끝, 알람 정지, 꺼질 때), OnDisable(), 테스트 메뉴
    /// [!] 흡착 중인 상자는 그대로 붙어 있다 (상태 "흡착 유지 · 정지").
    public void StopLoading()
    {
        stopRequested = true;
        if (attacher != null && attacher.Busy)
        {
            if (EndLine != null && EndLine.CurrentBox != null)
                { blockedLoads.Add(EndLine.CurrentBox); }
            attacher.StopLoading();
        }
        if (loop != null)
            { StopCoroutine(loop); }

        loop = null;
        status = attacher != null && attacher.Attached ? "흡착 유지 · 정지" : "정지";
    }

    // 적재에 실패해 마지막 구간에 남은 상자를 다시 시도하게 한다 (원인을 고친 뒤 부른다).
    /// [호출] CellController.RetryTerminalLoading(), 테스트 메뉴
    /// [주의] 로봇이 일하는 중이거나 상자를 든 채면 아무것도 하지 않는다.
    public void RetryFailed()
    {
        if (attacher == null || attacher.Busy || attacher.Attached) { return; }

        if (EndLine != null && EndLine.CurrentBox != null)
            { blockedLoads.Remove(EndLine.CurrentBox); }
        IsHalted = false;
        status = "감시 중";
    }

    // 수거함을 비운다. 들어 있는 상자를 지운다 (집계는 이미 Finish()에서 끝났다).
    /// [호출] 테스트 메뉴 (작업자 명령). 자동으로는 부르지 않는다.
    /// [주의] 로봇이 일하는 중이거나 상자를 든 채면 비우지 않는다 (작업 중 삭제 금지).
    public void EmptyContainer()
    {
        if (attacher == null || attacher.tote == null || attacher.Busy || attacher.Attached) { return; }

        int emptied = 0;
        foreach (var box in attacher.tote.Boxes)
            if (box != null)
                { Destroy(box.gameObject); emptied++; }
        EmptiedCount += emptied;
        if (cell != null && emptied > 0) { cell.LogEvent("컨테이너 비움", "", $"{terminal} {emptied}개"); }

        foreach (var box in attacher.tote.Boxes)
            if (box != null)
                { Destroy(box.gameObject); EmptiedCount++; }
    }


    //  ============================================================
    //  점검
    //  ============================================================
    // 연결 확인. 하나라도 빠지거나 어긋나면 이유를 error에 넣고 false.
    /// [호출] StartLoading(), CellController.ValidateTerminalConfiguration() (운전 시작 전, 에디터 도구)
    /// 로봇 · 수거함 · 슬롯 8개가 서로 같은 것을 가리키는지까지 본다.
    /// [참고] toto는 BoxAttacher.tote의 별칭 속성이다 (같은 수거함).
    public bool ValidateConfiguration(out string error)
    {
        error = null;

        //  1) 이 컴포넌트 · 터미널 · 이송 경로
        if (!isActiveAndEnabled || attacher == null || lane == null ||
            (int)terminal < (int)Destination.A || (int)terminal > (int)Destination.C || EndLine == null || EndLine.StopPoint == null)
        {
            error = "LoadSequence의 터미널/이송 경로/로봇 연결을 확인하세요.";
            return false;
        }

        //  2) 로봇 ↔ 수거함 서로 연결 (tcp · 흡착컵 · IK · 컨테이너 · 슬롯 수)
        var loader = attacher;
        var tote = loader.toto;
        if (!loader.isActiveAndEnabled || loader.tcp == null || loader.suctionCup == null || loader.armIK == null ||
            tote == null || !tote.isActiveAndEnabled || tote.loader != loader || loader.container != tote.transform ||
            loader.slots == null || tote.slots == null || loader.slots.Length != tote.Capacity || tote.slots.Length != tote.Capacity)
        {
            error = terminal + " 적재 로봇/수거함 연결을 확인하세요.";
            return false;
        }

        //  3) 슬롯: 빈 칸 없음, 로봇과 수거함이 같은 순서, 중복 없음, 수거함 아래에 있음
        var slots = new HashSet<Transform>();
        for (int i = 0; i < tote.Capacity; i++)
        {
            if (loader.slots[i] == null || loader.slots[i] != tote.slots[i] || !slots.Add(loader.slots[i]) || !loader.slots[i].IsChildOf(tote.transform))
            {
                error = terminal + " 수거함 슬롯이 누락되었거나 일치하지 않습니다.";
                return false;
            }
        }
        return true;
    }


    //  ============================================================
    //  적재 흐름 (코루틴)
    //  ============================================================
    // 감시 루프: 구간 사이 이송을 돕고, 마지막 구간에 선 상자를 하나씩 수거함에 넣는다. Running이 false가 되면 끝난다.
    IEnumerator LoadLoop()
    {
        while (Running)
        {
            //  1) 구간 사이 이송. 분기 구간이 아니라 NextLine 연결을 모두 지난 최종 정지점에서만 인계한다
            AdvanceTerminalTransfers(lane);
            var loadingLine = TerminalEnd(lane);
            if (loadingLine == null)
                { yield return null; continue; }

            //  2) 새 상자가 서고, 로봇이 쉬고 있을 때까지 기다린다 (막힌 상자 · 이미 끝난 상자는 건너뛴다)
            var box = loadingLine.CurrentBox;
            if (box == null || blockedLoads.Contains(box) || box.IsFinished || !attacher.IsReady || attacher.Busy || attacher.Attached)
                { yield return null; continue; }

            //  3) 정지 위치가 로봇 범위 밖이면 임시 분기 정지점이다. 이송 경로가 이어질 때까지 적재 · 실패 처리를 하지 않는다
            if (Vector3.Distance(attacher.transform.position, loadingLine.StopPoint.position) > maxPickupDistance)
            {
                box.state = BoxState.Transferring;
                if (waitingTransfers.Add(box))
                    { Debug.Log($"[Cell Transfer] {terminal} | {box.boxId} 이송 대기 | 최종 적재 지점 미연결: {loadingLine.name}. 로봇 앞 정지점까지 NextLine 경로를 연결하세요.", this); }
                yield return null;
                continue;
            }
            waitingTransfers.Remove(box);

            //  4) 상자 1개
            yield return LoadTerminalBox(loadingLine, box);
        }

        loop = null;
        status = "정지";
    }

    // 상자 1개: 확인 → TryLoadBox() → 끝날 때까지 대기 → 결과 처리. 벨트 위에 그대로면 1번 더 시도한다.
    /// [종료] 들어갔으면 레인에서 빼고 Finish(). 못 넣었으면 BlockTerminal().
    /// 성공 여부는 BoxAttacher.PlacedBoxes에 들어갔는지로 본다. LastLoadSucceeded는 팔이 운반 높이로 돌아왔는지까지 포함한다.
    /// [!] 수거함이 가득 차면 경고 후 비워질 때까지 여기서 기다리고, 끝나면 그냥 돌아간다. 같은 상자는 다음 루프에서 다시 집는다.
    IEnumerator LoadTerminalBox(ConveyorLine loadingLine, BoxInfo box)
    {
        var loader = attacher;
        float loadStarted = Time.time;
        status = $"적재 중 ({box.boxId})";

        //  1) 확인: NG 상자, Collider · Rigidbody, 집기 범위, 수거함 가득 참
        var collider = box.GetComponent<BoxCollider>();
        string error = null;
        if (box.IsMarked || box.destination == Destination.NG)
            { error = "NG 상자가 일반 터미널에 도착했습니다."; }
        else if (collider == null || collider.attachedRigidbody == null)
            { error = "상자의 Collider/Rigidbody가 없습니다."; }
        else if (Vector3.Distance(loader.transform.position, collider.bounds.center) > maxPickupDistance)
            { error = $"정지점이 로봇 집기 범위 밖입니다 ({Vector3.Distance(loader.transform.position, collider.bounds.center):F2}m). 터미널 이송 경로를 연결하세요."; }
        //  자동 비우기: 비우고 돌아간다. 다음 루프에서 같은 상자를 집는다 (Destroy는 프레임 끝에 반영)
        //if (autoEmpty)
        //{
        //    EmptyContainer();
        //    yield return null;
        //    yield break;
        //}
        else if (loader.toto.Count >= loader.toto.Capacity)
        {
            //  수거함이 비워지면 자동으로 다시 확인한다. 가득 참을 적재 실패로 반복 집계하지 않는다
            status = "수거함 가득 참 · 비우기 대기";
            blockedLoads.Add(box);
            Warn("수거함 가득 참 " + terminal, box.boxId);
            while (Running && !AlarmActive && loader.toto.Count >= loader.toto.Capacity)
                { yield return null; }
            blockedLoads.Remove(box);
            yield break;
        }
        if (error != null)
        {
            BlockTerminal(box, error);
            yield break;
        }

        //  2) 적재 (최대 2번)
        var body = collider.attachedRigidbody;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (AlarmActive || !Running || box == null) { yield break; }

            box.state = BoxState.Loading;
            if (!loader.TryLoadBox(collider, out error)) { break; }
            Debug.Log($"[Cell Loading] {terminal} | {box.boxId} 적재 시작 | 시도 {attempt + 1}", this);

            //  끝날 때까지 기다린다. 제한 시간을 넘으면 BoxAttacher를 멈춘다
            float started = Time.time;
            while (loader.Busy && !AlarmActive && Running && Time.time - started < loadTimeLimit)
                { yield return null; }
            bool timedOut = loader.Busy && Time.time - started >= loadTimeLimit;
            if (loader.Busy)
                { loader.StopLoading(); }

            //  들어갔으면: 레인에서 빼고 집계한다. 놓기는 성공했지만 팔 복귀가 실패한 경우도 실물 적재는 한 번만 센다
            bool placed = false;
            foreach (var item in loader.PlacedBoxes)
                if (item == collider)
                    { placed = true; break; }
            if (placed)
            {
                loadingLine.TakeAway(box);
                LoadedCount++;
                LastLoadSeconds = Time.time - loadStarted;
                if (cell != null)
                    { cell.Finish(box, terminal); }
                status = "감시 중";
                if (!loader.LastLoadSucceeded)
                    { Warn("적재 후 팔 복귀 실패 " + terminal, box.boxId); }
                yield break;
            }
            if (AlarmActive || !Running) { yield break; }

            //  못 넣었으면: 흡착 중이거나 상자가 정지 위치를 5 cm 넘게 벗어났으면 되돌리거나 다시 집지 않는다
            error = timedOut ? "적재 제한 시간 초과" : loader.Status;
            if (loader.Attached || 
                Vector3.Distance(collider.bounds.center, loadingLine.StopPoint.position + Vector3.up * collider.bounds.extents.y) > .05f) 
                { break; }
            if (attempt == 0)
            {
                Debug.LogWarning($"[Cell Loading] {box.boxId} 적재 재시도 | {error}", this);
                yield return new WaitForSeconds(.5f);
            }
        }

        //  3) 실패: 벨트 위 상자는 다시 kinematic으로 두고, 이 상자를 막는다
        if (!loader.Attached && body != null)
            { body.isKinematic = true; }
        BlockTerminal(box, error ?? "적재 실패");
    }

    // 적재 실패: 그 상자를 다시 집지 않게 막고 경고를 올린다. 작업자가 치우거나 RetryFailed()로 다시 시도한다.
    /// 상자를 든 채면 IsHalted를 켠다.
    void BlockTerminal(BoxInfo box, string error)
    {
        box.state = BoxState.Sorting;
        blockedLoads.Add(box);
        status = $"적재 실패 · 작업자 확인 대기 ({box.boxId})";
        IsHalted = attacher.Attached;
        Debug.LogWarning($"[Cell Loading] {terminal} | {box.boxId} 정지 유지 | {error}", this);
        Warn("적재 실패 " + terminal, box.boxId);
    }

    // 경고를 알린다. Cell 칸이 비어 있으면 Console 경고만 남긴다.
    void Warn(string type, string boxId)
    {
        if (cell != null)
            { cell.RaiseAlarm(type, boxId); }
        else
            { Debug.LogWarning($"[Load] {terminal} | {boxId} | {type}", this); }
    }


    //  ============================================================
    //  이송 경로 (터미널 레인 구간 잇기)
    //  ============================================================
    // 구간 사이 이송: 정지 위치에 상자가 있고 다음 구간이 받을 수 있으면 보낸다 (분기점 → 터미널 끝).
    /// 순환 연결이면 한 바퀴에서 멈춘다 (visited).
    void AdvanceTerminalTransfers(ConveyorLine start)
    {
        var visited = new HashSet<ConveyorLine>();
        for (var line = start; line != null && line.NextLine != null && visited.Add(line); line = line.NextLine)
        {
            if (!line.HasBoxAtStop || !line.NextLine.CanAccept) { continue; }

            line.CurrentBox.state = BoxState.Transferring;
            line.Release();
        }
    }

    // start에서 NextLine을 끝까지 따라간 마지막 구간. 순환하면 null
    /// [호출] EndLine, LoadLoop(), 에디터 도구
    public static ConveyorLine TerminalEnd(ConveyorLine start)
    {
        var visited = new HashSet<ConveyorLine>();
        var line = start;
        while (line != null)
        {
            if (!visited.Add(line)) { return null; }
            if (line.NextLine == null) { return line; }
            line = line.NextLine;
        }
        return null;
    }


    //  ============================================================
    //  테스트 (LoadSequence ⋮ 메뉴, Play 중에만)
    //  ============================================================
    [ContextMenu("테스트/감시 시작")]
    void TestStart()
    {
        if (Application.isPlaying) { StartLoading(); }
    }

    [ContextMenu("테스트/감시 정지")]
    void TestStop()
    {
        if (Application.isPlaying) { StopLoading(); }
    }

    [ContextMenu("테스트/실패 상자 다시 시도")]
    void TestRetry()
    {
        if (Application.isPlaying) { RetryFailed(); }
    }

    [ContextMenu("테스트/컨테이너 비우기")]
    void TestEmpty()
    {
        if (Application.isPlaying) { EmptyContainer(); }
    }
}
