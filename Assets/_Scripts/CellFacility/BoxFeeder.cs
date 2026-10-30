using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//  ============================================================
//  BoxFeeder
//  설정된 간격마다 상자를 만들어 교정 구간 컨베이어 입구에 올리는 투입 스크립트.
//      - 송장면 방향을 비율대로 정하고(위 70%, 측면 5%씩, 바닥 10%), 송장 번호와 주소 지역을 정답으로 기록한다.
//      - 송장 글자는 LabelLayout 에셋의 칸 위치대로 찍는다 (판독 스테이션 · 판독 학습과 같은 에셋).
//      - 벨트 위 대기 상자가 maxQueue에 닿으면 투입을 잠시 멈추고, resumeQueue 이하로 줄면 다시 넣는다.
//      - noLabelRate · wrongLabelRate만큼 송장 미부착 · 규칙 위반 상자를 일부러 섞는다 (평소 0).
//
//  [사용]   빈 오브젝트 "Feeder"에 붙이고 Cell · Line · Box Prefab · Layout 칸을 채운다. CellController의 Feeder 칸에 넣는다.
//  [!] Box Prefab은 TMP 글자(Number Text · Region Text)가 연결된 공정용 Variant를 넣는다. 비어 있으면 투입하지 않는다.
//  [호출]   CellController.Run()이 StartFeeding(), 사이클 정지 · 알람이 StopFeeding()을 부른다.
//  [!] 투입 간격 · 비율 · 대기 한도는 Cell의 Settings(실행 중 복사본)에서 읽는다. Play 전에 CellSettings 에셋에서 바꾼다.
//
//  [설계서와의 차이]
//      * StartFeeding(count)로 개수를 받는다. 0이면 멈출 때까지, 1 이상이면 count개를 넣고 멈춘다 (Run(n)도 같은 루프).
//      * InProcess(처리 중인 상자 수)를 추가했다. 투입한 상자 중 씬에 남아 있고 Finish()되지 않은 상자를 센다.
//        분류 구간이 없는 테스트 씬에서는 출구에서 지워지는 상자가 있어 "FedCount − Processed"로는 셀 수 없다.
//      * MakeLabel()은 송장 글자를 직접 쓰지 않고 (번호, 지역, 위반 종류)를 돌려준다. 글자는 LabelLayout.Print()가 찍는다.
//        위반 종류(error)는 로그와 테스트 통계에만 쓴다.
//      * 테스트 메뉴 "1000번 뽑아 비율 보기"를 추가했다.
//  ============================================================
public class BoxFeeder : MonoBehaviour
{
    //  ---------------- 연결 ----------------    (FR-01)
    [Header("연결")]
    [Tooltip("공정 총괄 (CellController). 설정(Settings)을 읽고, 투입 지연을 알린다")]
    [SerializeField] CellController cell;

    [Tooltip("상자를 올릴 컨베이어 (교정 구간 CorrectLine)")]
    [SerializeField] ConveyorLine line;

    [Tooltip("투입할 상자 Prefab (ParcelBox_6x8x12cm_Process)")]
    [SerializeField] BoxInfo boxPrefab;

    [Tooltip("송장 배치 에셋 (LabelLayout). 송장 번호 · 지역을 칸 위치대로 찍는다")]
    [SerializeField] LabelLayout layout;

    //  ---------------- 투입 지연 ----------------    (FR-01)
    [Header("투입 지연")]
    [Tooltip("입구가 차 있을 때 다시 시도하는 간격(초) (default: 0.5)")]
    [Min(0.1f)][SerializeField] float feedRetryDelay = 0.5f;

    [Tooltip("입구가 이 시간(초) 넘게 막혀 있으면 \"투입 지연\" 경고 (default: 10)")]
    [Min(1f)][SerializeField] float feedDelayWarn = 10f;

    //  ---------------- 테스트 ----------------
    [Header("테스트")]
    [Tooltip("켜면 화면에 테스트 패널을 그린다")]
    [SerializeField] bool showTestButtons = false;

    [Tooltip("테스트 패널 위치 (x, y). 컨베이어 패널과 겹치지 않게 바꾼다")]
    [SerializeField] Vector2 guiPosition = new Vector2(10, 120);


    //  ============================================================
    //  상태 (CellController · 대시보드가 읽는 값)
    //  ============================================================
    /// 투입한 상자 수 (Play 시작부터)    (FR-10)
    public int FedCount { get; private set; }

    /// 연속 투입 중인지 (StartFeeding() ~ StopFeeding() 또는 count개 투입 끝)
    public bool IsFeeding { get; private set; }

    /// 대기 상자가 많아 투입을 잠시 멈췄는지    (FR-13)
    public bool IsPaused { get; private set; }

    /// 처리 중인 상자 수: 투입한 상자 중 씬에 남아 있고 Finish()되지 않은 상자.
    /// 지워졌거나 끝난 상자는 목록에서 뺀 뒤 센다.
    public int InProcess
    {
        get
        {
            fedBoxes.RemoveAll(box => box == null || box.IsFinished);
            return fedBoxes.Count;
        }
    }

    /// Cell의 실행 중 설정 (Play 전이거나 Cell이 비어 있으면 null)
    CellSettings Settings => cell != null ? cell.Settings : null;

    readonly List<BoxInfo> fedBoxes = new List<BoxInfo>();     //  투입한 상자 (처리 중인 상자 수 세기용)
    Coroutine feedRoutine;                                      //  지금 돌고 있는 연속 투입 코루틴
    int nextSerial;                                             //  마지막으로 쓴 일련번호 (상자 ID · 물품번호)

    static readonly BoxFace[] Sides = { BoxFace.Front, BoxFace.Back, BoxFace.Left, BoxFace.Right };


    //  ============================================================
    //  투입 명령 (CellController · 테스트가 부른다)
    //  ============================================================
    // 연속 투입 시작. count = 0이면 StopFeeding()까지, 1 이상이면 count개를 넣고 멈춘다.
    /// [호출] CellController.Run()
    public void StartFeeding(int count = 0)
    {
        if (IsFeeding || !Ready()) { return; }

        IsFeeding = true;
        feedRoutine = StartCoroutine(FeedLoop(Mathf.Max(0, count)));
        Debug.Log($"[Feeder] 투입 시작 | {(count > 0 ? $"상자 {count}개" : "연속")} | 간격 {Settings.feedInterval:0.#}초", this);
    }

    // 연속 투입 정지. 이미 넣은 상자는 그대로 흘러간다.
    /// [호출] CellController.RequestCycleStop(), 알람 정지, 운전 끝
    public void StopFeeding()
    {
        if (!IsFeeding) { return; }

        if (feedRoutine != null) { StopCoroutine(feedRoutine); }
        feedRoutine = null;
        IsFeeding = false;
        IsPaused = false;
        Debug.Log($"[Feeder] 투입 정지 | 지금까지 {FedCount}개", this);
    }

    // 상자 1개를 만들어 입구에 올린다. 입구가 차 있으면 만들지 않고 null.
    /// [호출] FeedLoop(), 테스트 메뉴 · 패널
    /// 순서: 송장면 · 송장 정하기 → 상자 만들기 → 투입 정보 기록 → 컨베이어에 올리기
    public BoxInfo FeedOne()
    {
        if (!Ready() || !line.CanAccept) { return null; }

        CellSettings settings = Settings;
        int serial = nextSerial + 1;

        //  1) 송장면 방향과 송장 내용을 정한다. 송장 미부착 상자는 번호 · 지역이 없다
        BoxFace face = PickFace(settings);
        bool hasLabel = Random.value >= settings.noLabelRate;
        (string number, string region, string error) = hasLabel ? MakeLabel(settings, serial) : ("", "", "");

        //  2) 상자를 만든다. 송장면이 face를 향하고, 위쪽 축 둘레로 0 · 90 · 180 · 270° 중 하나만큼 돌아간다
        Quaternion rotation = BoxInfo.RotationFor(face, 90f * Random.Range(0, 4));
        BoxInfo box = Instantiate(boxPrefab, transform.position, rotation);

        //  3) 투입 정보 기록 (정답은 학습 보상과 분류 성공 판정에만 쓴다)
        box.boxId = $"BOX-{serial:0000}";
        box.name = box.boxId;
        box.createdTime = Time.time;
        box.initialFace = face;
        box.hasLabel = hasLabel;
        box.labelNumber = number;
        box.labelRegion = region;

        //  송장 글자를 칸 위치대로 찍는다. 송장 미부착 상자는 송장 오브젝트만 끈다 (판독 스테이션에서 NoLabel)
        if (hasLabel) { layout.Print(box, number, region); }
        else if (box.label != null) { box.label.gameObject.SetActive(false); }

        //  4) 컨베이어 입구에 올린다 (위치 · 물리는 ConveyorLine.Feed()가 맞춘다)
        if (!line.Feed(box))
        {
            Destroy(box.gameObject);
            return null;
        }

        nextSerial = serial;
        FedCount++;
        fedBoxes.Add(box);

        string labelText = hasLabel ? $"{number} {region}" : "*송장 없음";
        string errorText = error != "" ? $" *규칙 위반({error})" : "";
        Debug.Log($"[Feeder] {box.boxId} 투입 | 송장면 {face} | {labelText}{errorText}", this);
        return box;
    }


    //  ============================================================
    //  투입 흐름 (코루틴)
    //  ============================================================
    // 대기 상자 확인 → 투입(입구가 빌 때까지 재시도) → 투입 간격 대기를 반복한다.
    /// 투입 간격은 매번 Settings에서 읽으므로, 운전 제어 패널에서 바꾸면 다음 상자부터 적용된다.
    IEnumerator FeedLoop(int count)
    {
        int fed = 0;
        while (count == 0 || fed < count)
        {
            //  1) 대기 상자가 많으면 줄 때까지 기다린다    (FR-13)
            yield return WaitQueue();

            //  2) 입구가 빌 때까지 feedRetryDelay마다 다시 시도한다. feedDelayWarn을 넘으면 "투입 지연" 경고 (상자당 한 번)
            float blockedSince = Time.time;
            bool warned = false;
            while (FeedOne() == null)
            {
                if (!warned && Time.time - blockedSince >= feedDelayWarn)
                {
                    warned = true;
                    cell.RaiseAlarm("투입 지연", $"BOX-{nextSerial + 1:0000}");
                }
                yield return new WaitForSeconds(feedRetryDelay);
            }
            fed++;

            //  3) 다음 상자까지 투입 간격만큼 기다린다 (마지막 상자 뒤에는 기다리지 않는다)
            if (count == 0 || fed < count)
            { yield return new WaitForSeconds(Settings.feedInterval); }
        }

        feedRoutine = null;
        IsFeeding = false;
        Debug.Log($"[Feeder] 투입 끝 | 상자 {count}개", this);
    }

    // 벨트 위 대기 상자가 maxQueue 이상이면 resumeQueue 이하로 줄 때까지 투입을 멈춘다.
    /// 멈추는 기준과 다시 넣는 기준을 다르게 두어, 한 개 차이로 멈췄다 넣었다를 반복하지 않게 한다.
    IEnumerator WaitQueue()
    {
        if (line.QueueCount < Settings.maxQueue) { yield break; }

        IsPaused = true;
        Debug.Log($"[Feeder] 대기 상자 {line.QueueCount}개 → 투입 일시 정지", this);

        while (line.QueueCount > Settings.resumeQueue) { yield return null; }

        IsPaused = false;
        Debug.Log($"[Feeder] 대기 상자 {line.QueueCount}개 → 투입 재개", this);
    }


    //  ============================================================
    //  송장면 · 송장 정하기
    //  ============================================================
    // 비율에 따라 송장면 방향을 고른다: 위 faceTop, 앞 · 뒤 · 왼쪽 · 오른쪽 각 faceSide, 나머지는 바닥.
    /// 0 ~ 1 무작위 값을 비율 구간에 차례로 대어 본다. 비율의 합이 1이 아니면 남는 몫은 바닥이 된다 (CellSettings가 경고).
    static BoxFace PickFace(CellSettings settings)
    {
        float roll = Random.value;
        if (roll < settings.faceTop) { return BoxFace.Top; }
        roll -= settings.faceTop;

        foreach (BoxFace side in Sides)
        {
            if (roll < settings.faceSide) { return side; }
            roll -= settings.faceSide;
        }
        return BoxFace.Bottom;
    }

    // 송장 번호 5자리와 주소 지역을 정한다. 지역 코드는 고르게, 물품번호 3자리는 일련번호의 끝 3자리.
    /// wrongLabelRate 확률로 규칙 위반 송장을 만든다. 세 종류 중 하나를 고른다.
    ///     코드 오류 (예: 23137), 자리수 오류 (예: 2213), 주소 불일치 (예: 11001인데 주소 부산)
    /// error는 위반 종류 (정상이면 ""). 로그와 테스트 통계에만 쓴다.
    static (string number, string region, string error) MakeLabel(CellSettings settings, int serial)
    {
        int count = settings.regionCodes.Length;
        int index = Random.Range(0, count);
        int code = settings.regionCodes[index];
        string item = (serial % 1000).ToString("000");

        if (Random.value >= settings.wrongLabelRate)
        { return ($"{code:00}{item}", RegionName(settings, index), ""); }

        //  지역이 1개뿐이면 주소 불일치를 만들 수 없으므로 앞의 두 종류에서만 고른다
        int kind = Random.Range(0, count > 1 ? 3 : 2);
        switch (kind)
        {
            case 0:     //  코드 오류: 표에 없는 지역 코드
                {
                    int wrongCode;
                    do { wrongCode = Random.Range(10, 100); }
                    while (System.Array.IndexOf(settings.regionCodes, wrongCode) >= 0);
                    return ($"{wrongCode:00}{item}", RegionName(settings, index), "코드 오류");
                }
            case 1:     //  자리수 오류: 4자리
                {
                    return ($"{code:00}{serial % 100:00}", RegionName(settings, index), "자리수 오류");
                }
            default:    //  주소 불일치: 코드와 다른 지역 이름
                {
                    int other = (index + Random.Range(1, count)) % count;
                    return ($"{code:00}{item}", RegionName(settings, other), "주소 불일치");
                }
        }
    }

    // index번째 지역 이름 (표의 길이가 다르면 "")
    static string RegionName(CellSettings settings, int index)
    {
        return index < settings.regionNames.Length ? settings.regionNames[index] : "";
    }


    //  ============================================================
    //  점검
    //  ============================================================
    // 필수 칸과 설정 확인. 빠진 것이 있으면 이름을 알려 주고 false.
    bool Ready()
    {
        string missing = cell == null ? "Cell"
                       : line == null ? "Line"
                       : boxPrefab == null ? "Box Prefab"
                       : layout == null ? "Layout"
                       : null;
        if (missing != null)
        {
            Debug.LogWarning($"[Feeder] {missing} 칸이 비어 있어 투입하지 않습니다.", this);
            return false;
        }
        if (boxPrefab.numberText == null || boxPrefab.regionText == null)
        {
            Debug.LogWarning("[Feeder] Box Prefab에 TMP 글자(Number Text · Region Text)가 연결되어 있지 않습니다. 공정용 Variant를 넣으세요.", this);
            return false;
        }
        if (Settings == null || Settings.regionCodes.Length == 0)
        {
            Debug.LogWarning("[Feeder] Cell의 Settings가 없습니다. Play 중인지, Cell에 CellSettings가 들어 있는지 확인하세요.", this);
            return false;
        }
        return true;
    }


    //  ============================================================
    //  테스트 (BoxFeeder ⋮ 메뉴, Play 중에만)
    //  ============================================================
    // [테스트] 상자 1개 투입. 입구가 차 있으면 알려 준다.
    [ContextMenu("테스트/상자 1개 투입")]
    void TestFeedOne()
    {
        if (!Application.isPlaying || !Ready()) { return; }

        if (!line.CanAccept) { Debug.Log("[Feeder] 입구가 차 있습니다.", this); }
        else { FeedOne(); }
    }

    [ContextMenu("테스트/연속 투입 시작")]
    void TestStartFeeding()
    {
        if (Application.isPlaying) { StartFeeding(); }
    }

    [ContextMenu("테스트/연속 투입 정지")]
    void TestStopFeeding()
    {
        if (Application.isPlaying) { StopFeeding(); }
    }

    // [테스트] 상자를 만들지 않고 송장면 · 송장만 1000번 뽑아 비율을 본다.
    /// 기본값이면 위 약 700, 측면 약 50씩, 바닥 약 100. noLabelRate · wrongLabelRate를 올리면 그 비율만큼 나온다.
    [ContextMenu("테스트/1000번 뽑아 비율 보기")]
    void TestStats()
    {
        if (!Application.isPlaying || !Ready()) { return; }

        CellSettings settings = Settings;
        int[] faces = new int[6];
        int noLabel = 0, codeError = 0, digitError = 0, mismatch = 0;
        for (int i = 0; i < 1000; i++)
        {
            faces[(int)PickFace(settings)]++;
            if (Random.value < settings.noLabelRate)
            { noLabel++; continue; }

            string error = MakeLabel(settings, i + 1).error;
            if (error == "코드 오류") { codeError++; }
            else if (error == "자리수 오류") { digitError++; }
            else if (error == "주소 불일치") { mismatch++; }
        }

        Debug.Log($"[Feeder] 1000번 | 위 {faces[0]} · 앞 {faces[1]} · 뒤 {faces[2]} · 왼쪽 {faces[3]} · 오른쪽 {faces[4]} · 바닥 {faces[5]} | " +
                  $"송장 없음 {noLabel} | 규칙 위반 {codeError + digitError + mismatch} (코드 오류 {codeError} · 자리수 오류 {digitError} · 주소 불일치 {mismatch})", this);
    }

    // 테스트 패널 (showTestButtons가 켜져 있을 때만)
    void OnGUI()
    {
        if (!showTestButtons) { return; }

        int queue = line != null ? line.QueueCount : 0;
        GUILayout.BeginArea(new Rect(guiPosition.x, guiPosition.y, 240, 80), GUI.skin.box);
        GUILayout.Label($"[투입] {FedCount}개 | 대기 {queue}개{(IsPaused ? " | 일시 정지" : "")}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1개 투입")) { TestFeedOne(); }
        if (GUILayout.Button(IsFeeding ? "연속 정지" : "연속 시작"))
        {
            if (IsFeeding) { StopFeeding(); }
            else { StartFeeding(); }
        }
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
}
