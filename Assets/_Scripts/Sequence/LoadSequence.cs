using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 로봇별 터미널 이송/적재 담당. CellController는 운전과 최종 집계만 맡는다.
// 흡착/IK/슬롯 배치는 BoxAttacher와 ToteBase를 사용한다.
// 만적 시 자동 삭제하지 않고 비워질 때까지 기다린다. 실패 상자는 작업자 재시도 전까지 보존한다.
[DisallowMultipleComponent]
public class LoadSequence : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] BoxAttacher attacher;
    [SerializeField] ConveyorLine lane;
    [SerializeField] Destination terminal = Destination.B;
    [SerializeField] CellController cell;
    [Header("동작")]
    [Min(.1f)][SerializeField] float maxPickupDistance = 1f;
    [Min(1f)][SerializeField] float loadTimeLimit = 180f;
    [SerializeField] string status = "대기";
    public BoxAttacher Attacher => attacher;
    public ConveyorLine Lane => lane;
    public ConveyorLine EndLine => TerminalEnd(lane);
    public Destination Terminal => terminal;
    public CellController Cell => cell;
    public bool IsLoading => loop != null;
    public bool IsHalted { get; private set; }
    public string Status => status;
    public int LoadedCount { get; private set; }
    public int EmptiedCount { get; private set; }
    public float LastLoadSeconds { get; private set; }
    bool AlarmActive => cell != null && cell.IsAlarmActive;
    bool Running => !stopRequested && (cell == null || cell.IsRunning) && !AlarmActive;
    Coroutine loop;
    bool stopRequested;
    readonly HashSet<BoxInfo> blockedLoads = new HashSet<BoxInfo>();
    readonly HashSet<BoxInfo> waitingTransfers = new HashSet<BoxInfo>();
    void Awake() { if (attacher == null) attacher = GetComponent<BoxAttacher>(); }

    public void StartLoading()
    {
        if (loop != null) return;
        if (!ValidateConfiguration(out string error)) { Debug.LogWarning("[Load] " + error, this); return; }
        stopRequested = false; IsHalted = false; status = "감시 중";
        loop = StartCoroutine(LoadLoop());
    }
    // 알람/비활성 시 기존 Cell 동작처럼 즉시 중단한다. 흡착 중 상자는 유지한다.
    public void StopLoading()
    {
        stopRequested = true;
        if (attacher != null && attacher.Busy)
        {
            if (EndLine != null && EndLine.CurrentBox != null) blockedLoads.Add(EndLine.CurrentBox);
            attacher.StopLoading();
        }
        if (loop != null) StopCoroutine(loop);
        loop = null; status = attacher != null && attacher.Attached ? "흡착 유지 · 정지" : "정지";
    }
    public void RetryFailed()
    {
        if (attacher == null || attacher.Busy || attacher.Attached) return;
        if (EndLine != null && EndLine.CurrentBox != null) blockedLoads.Remove(EndLine.CurrentBox);
        IsHalted = false; status = "감시 중";
    }
    // 명시적인 작업자 명령만 컨테이너를 비운다. 작업 중 삭제는 허용하지 않는다.
    public void EmptyContainer()
    {
        if (attacher == null || attacher.tote == null || attacher.Busy || attacher.Attached) return;
        foreach (var box in attacher.tote.Boxes)
            if (box != null) { Destroy(box.gameObject); EmptiedCount++; }
    }
    public bool ValidateConfiguration(out string error)
    {
        error = null;
        if (!isActiveAndEnabled || attacher == null || lane == null ||
            (int)terminal < (int)Destination.A || (int)terminal > (int)Destination.C || EndLine == null || EndLine.StopPoint == null)
        { error = "LoadSequence의 터미널/이송 경로/로봇 연결을 확인하세요."; return false; }
        var loader = attacher;
        var tote = loader.toto;
        if (!loader.isActiveAndEnabled || loader.tcp == null || loader.suctionCup == null || loader.armIK == null ||
            tote == null || !tote.isActiveAndEnabled || tote.loader != loader || loader.container != tote.transform ||
            loader.slots == null || tote.slots == null || loader.slots.Length != tote.Capacity || tote.slots.Length != tote.Capacity)
        { error = terminal + " 적재 로봇/수거함 연결을 확인하세요."; return false; }
        var slots = new HashSet<Transform>();
        for (int i = 0; i < tote.Capacity; i++)
            if (loader.slots[i] == null || loader.slots[i] != tote.slots[i] || !slots.Add(loader.slots[i]) || !loader.slots[i].IsChildOf(tote.transform))
            { error = terminal + " 수거함 슬롯이 누락되었거나 일치하지 않습니다."; return false; }
        return true;
    }

    IEnumerator LoadLoop()
    {
        while (Running)
        {
            AdvanceTerminalTransfers(lane);
            // 분기 구간이 아니라 NextLine 연결을 모두 지난 최종 정지점에서만 인계한다.
            var loadingLine = TerminalEnd(lane);
            if (loadingLine == null) { yield return null; continue; }
            var box = loadingLine.CurrentBox;
            if (box == null || blockedLoads.Contains(box) || box.IsFinished || !attacher.IsReady || attacher.Busy || attacher.Attached)
            { yield return null; continue; }
            if (Vector3.Distance(attacher.transform.position, loadingLine.StopPoint.position) > maxPickupDistance)
            {
                // 임시 분기 정지점은 적재 지점이 아니다. 이송 경로가 이어질 때까지 적재/실패 처리를 하지 않는다.
                box.state = BoxState.Transferring;
                if (waitingTransfers.Add(box))
                    Debug.Log($"[Cell Transfer] {terminal} | {box.boxId} 이송 대기 | 최종 적재 지점 미연결: {loadingLine.name}. 로봇 앞 정지점까지 NextLine 경로를 연결하세요.", this);
                yield return null;
                continue;
            }
            waitingTransfers.Remove(box);
            yield return LoadTerminalBox(loadingLine, box);
        }
        loop = null; status = "정지";
    }

    void AdvanceTerminalTransfers(ConveyorLine start)
    {
        var visited = new HashSet<ConveyorLine>();
        for (var line = start; line != null && line.NextLine != null && visited.Add(line); line = line.NextLine)
        {
            if (!line.HasBoxAtStop || !line.NextLine.CanAccept) continue;
            line.CurrentBox.state = BoxState.Transferring;
            line.Release();
        }
    }

    public static ConveyorLine TerminalEnd(ConveyorLine start)
    {
        var visited = new HashSet<ConveyorLine>();
        var line = start;
        while (line != null)
        {
            if (!visited.Add(line)) return null;
            if (line.NextLine == null) return line;
            line = line.NextLine;
        }
        return null;
    }

    IEnumerator LoadTerminalBox(ConveyorLine loadingLine, BoxInfo box)
    {
        var loader = attacher;
        float loadStarted = Time.time;
        status = $"적재 중 ({box.boxId})";
        var collider = box.GetComponent<BoxCollider>();
        string error = null;
        if (box.IsMarked || box.destination == Destination.NG) error = "NG 상자가 일반 터미널에 도착했습니다.";
        else if (collider == null || collider.attachedRigidbody == null) error = "상자의 Collider/Rigidbody가 없습니다.";
        else if (Vector3.Distance(loader.transform.position, collider.bounds.center) > maxPickupDistance)
            error = $"정지점이 로봇 집기 범위 밖입니다 ({Vector3.Distance(loader.transform.position, collider.bounds.center):F2}m). 터미널 이송 경로를 연결하세요.";
        else if (loader.toto.Count >= loader.toto.Capacity)
        {
            // 수거함이 비워지면 자동으로 다시 확인한다. 만적을 적재 실패로 반복 집계하지 않는다.
            status = "수거함 가득 참 · 비우기 대기";
            blockedLoads.Add(box);
            Warn("수거함 가득 참 " + terminal, box.boxId);
            while (Running && !AlarmActive && loader.toto.Count >= loader.toto.Capacity) yield return null;
            blockedLoads.Remove(box);
            yield break;
        }
        if (error != null) { BlockTerminal(box, error); yield break; }
        var body = collider.attachedRigidbody;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (AlarmActive || !Running || box == null) yield break;
            box.state = BoxState.Loading;
            if (!loader.TryLoadBox(collider, out error)) break;
            Debug.Log($"[Cell Loading] {terminal} | {box.boxId} 적재 시작 | 시도 {attempt + 1}", this);
            float started = Time.time;
            while (loader.Busy && !AlarmActive && Running && Time.time - started < loadTimeLimit) yield return null;
            bool timedOut = loader.Busy && Time.time - started >= loadTimeLimit;
            if (loader.Busy) loader.StopLoading();
            // 배치는 성공했지만 팔 복귀가 실패한 경우도 실물 적재는 한 번만 집계한다.
            bool placed = false;
            foreach (var item in loader.PlacedBoxes) if (item == collider) { placed = true; break; }
            if (placed)
            {
                loadingLine.TakeAway(box);
                LoadedCount++;
                LastLoadSeconds = Time.time - loadStarted;
                if (cell != null) cell.Finish(box, terminal);
                status = "감시 중";
                if (!loader.LastLoadSucceeded) Warn("적재 후 팔 복귀 실패 " + terminal, box.boxId);
                yield break;
            }
            if (AlarmActive || !Running) yield break;
            error = timedOut ? "적재 제한 시간 초과" : loader.Status;
            // 흡착 중이거나 이미 이동한 상자를 자동으로 되돌리거나 다시 집지 않는다.
            if (loader.Attached || Vector3.Distance(collider.bounds.center, loadingLine.StopPoint.position + Vector3.up * collider.bounds.extents.y) > .05f) break;
            if (attempt == 0) { Debug.LogWarning($"[Cell Loading] {box.boxId} 적재 재시도 | {error}", this); yield return new WaitForSeconds(.5f); }
        }
        if (!loader.Attached && body != null) body.isKinematic = true;
        BlockTerminal(box, error ?? "적재 실패");
    }

    void BlockTerminal(BoxInfo box, string error)
    {
        box.state = BoxState.Sorting;
        blockedLoads.Add(box);
        status = $"적재 실패 · 작업자 확인 대기 ({box.boxId})";
        IsHalted = attacher.Attached;
        Debug.LogWarning($"[Cell Loading] {terminal} | {box.boxId} 정지 유지 | {error}", this);
        Warn("적재 실패 " + terminal, box.boxId);
    }

    void Warn(string type, string boxId)
    {
        if (cell != null) cell.RaiseAlarm(type, boxId);
        else Debug.LogWarning($"[Load] {terminal} | {boxId} | {type}", this);
    }
    void OnDisable() { StopLoading(); }
    [ContextMenu("테스트/감시 시작")] void TestStart() { if (Application.isPlaying) StartLoading(); }
    [ContextMenu("테스트/감시 정지")] void TestStop() { if (Application.isPlaying) StopLoading(); }
    [ContextMenu("테스트/실패 상자 다시 시도")] void TestRetry() { if (Application.isPlaying) RetryFailed(); }
    [ContextMenu("테스트/컨테이너 비우기")] void TestEmpty() { if (Application.isPlaying) EmptyContainer(); }
}
