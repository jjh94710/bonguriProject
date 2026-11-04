using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

//  ============================================================
//  ProductionLog
//  세션 로그를 CSV 파일 2개로 남긴다.
//      - 상자 파일: 상자 1개가 끝날 때마다 1줄 (측정 · 교정 · 판독 · 결과 · 구간 시간).
//      - 이벤트 파일: 이상 이벤트, 제어 방식 전환, 알람 정지 · 해제마다 1줄. Play를 끝낼 때 세션 요약 1줄.
//
//  [사용] CellController와 같은 오브젝트("Cell")에 붙이고, CellController의 Log 칸에 넣는다.
//  [파일] 프로젝트 폴더/Reports/boxes_날짜_시각.csv, events_날짜_시각.csv (Assets 폴더 옆, Play할 때마다 새 파일)
//         처음 기록할 때 두 파일을 함께 만든다. 아무것도 기록하지 않은 Play는 파일을 만들지 않는다.
//  [!] 엑셀은 Play를 멈춘 뒤 연다 (Play 중에는 Unity가 파일을 쓰고 있다).
//  [!] 한 줄 쓸 때마다 Flush한다. 에디터가 멈춰도 그 전까지의 기록은 남는다.
//  [표시] enum은 이름(예: NoLabel)을 그대로 쓴다. 소수점은 나라 설정과 상관없이 점(.)으로 쓴다.
//
//  [설계서와의 차이]
//      * WriteBox()가 결과 글자(result)를 함께 받는다. 분류 성공 · 실패는 CellController.Finish()가 정답과 비교해서 알기 때문.
//      * 판독 신뢰도 열은 없다. LabelReader가 BoxInfo에 신뢰도를 남기지 않아 모름 칸 수로 대신한다.
//      * 세션 요약은 이벤트 파일의 마지막 줄(종류 "세션 요약")에 쓴다. 제어 방식별 교정 성공률 · 평균 회전 · 평균 교정 시간을 함께 쓴다.
//      * 알람 정지 · 해제, 분류 실패, 컨테이너 비움도 이벤트로 남긴다 (CellController가 부른다).
//  ============================================================
[DisallowMultipleComponent]
public class ProductionLog : MonoBehaviour
{
    //  ---------------- 파일 ----------------    (FR-11, FR-14)
    [Header("파일")]
    [Tooltip("로그 폴더 이름. 프로젝트 폴더(Assets 옆) 아래에 만든다 (default: Reports)")]
    [SerializeField] string folderName = "Reports";


    //  ============================================================
    //  상태 (운전 제어 패널 · 테스트가 읽는 값)
    //  ============================================================
    /// 만든 파일의 전체 경로 (만들기 전에는 빈 글자)
    public string BoxFilePath { get; private set; } = "";
    public string EventFilePath { get; private set; } = "";

    /// 지금까지 쓴 상자 줄 수, 이벤트 줄 수
    public int BoxLines { get; private set; }
    public int EventLines { get; private set; }

    StreamWriter boxWriter;             //  상자 파일 (처음 기록할 때 연다)
    StreamWriter eventWriter;           //  이벤트 파일 (상자 파일과 함께 연다)
    bool closed;                        //  파일을 닫았는지 (Play 종료 뒤에는 더 쓰지 않는다)
    CellController cell;                //  세션 요약에 쓸 집계 (같은 오브젝트)

    //  세션 요약용 (첫 투입 ~ 마지막 완료 시각)
    float firstCreated = -1f;
    float lastFinished = -1f;

    //  제어 방식별 집계. 칸 번호 = ControlMode 순서 (0 Basic, 1 Policy)    (FR-14)
    readonly int[] modeBoxes = new int[2];              //  송장이 붙은 상자 수
    readonly int[] modeCorrected = new int[2];          //  그중 교정에 성공한 상자 수
    readonly int[] modeRotations = new int[2];          //  회전 횟수 합
    readonly float[] modeCorrectSeconds = new float[2]; //  교정 → 판독 시간 합
    readonly int[] modeTimed = new int[2];              //  교정 → 판독 시간을 잰 상자 수


    //  ============================================================
    //  시작 · 끝
    //  ============================================================
    // 집계를 읽을 CellController를 찾아 둔다.
    void Awake()
    {
        cell = GetComponent<CellController>();
    }

    // 오브젝트가 없어질 때(Play 종료 포함) 세션 요약을 쓰고 파일을 닫는다.
    void OnDestroy()
    {
        if (boxWriter == null) { return; }      //  기록한 것이 없으면 파일도 없다

        WriteSummary(cell);
        Close();
    }


    //  ============================================================
    //  기록 (CellController가 부른다)
    //  ============================================================
    // 상자 1개의 처리 기록을 상자 파일에 1줄 쓴다. result = "분류 성공", "분류 실패 (정답 B)", "예외 (NoLabel)" 같은 결과 글자.
    /// [호출] CellController.Finish() (최종 상태를 정한 뒤)
    /// 열 순서 = Open()의 제목 줄. 정하지 않은 값(도착하지 않은 구간 시간 등)은 빈 칸으로 둔다.
    public void WriteBox(BoxInfo box, string result)
    {
        if (box == null || !Open()) { return; }

        string[] columns =
        {
            box.boxId, F(box.createdTime), box.initialFace.ToString(),
            Ratio(box, 0), Ratio(box, 1), Ratio(box, 2), Ratio(box, 3), Ratio(box, 4),
            box.judgedFace.ToString(), box.mode.ToString(),
            box.rotationCount.ToString(), box.flipped ? "1" : "0",
            $"{box.chosenRotation.x:0} {box.chosenRotation.y:0} {box.chosenRotation.z:0}",
            box.labelFound ? "1" : "0", box.readText, box.readRegion,
            box.unknownCount.ToString(), box.retryCount.ToString(),
            box.check.ToString(), box.regionCode.ToString(),
            box.destination.ToString(), box.actualDestination.ToString(),
            result, box.reason.ToString(),
            box.labelNumber, box.labelRegion,
            Span(box.createdTime, box.correctArriveTime), Span(box.correctArriveTime, box.readArriveTime),
            Span(box.readArriveTime, box.finishTime), Span(box.createdTime, box.finishTime),
        };
        WriteLine(boxWriter, columns);
        BoxLines++;

        AddToSummary(box);
    }

    // 이벤트 1건을 이벤트 파일에 1줄 쓴다 (발생 시각, 종류, 상자 ID, 내용).
    /// [호출] CellController.RaiseAlarm() (이상 이벤트), 알람 정지 · 해제, 분류 실패, 컨테이너 비움
    public void WriteEvent(string type, string boxId, string detail = "")
    {
        if (!Open()) { return; }

        WriteLine(eventWriter, new[] { F(Time.time), System.DateTime.Now.ToString("HH:mm:ss"), type, boxId, detail });
        EventLines++;
    }

    // 제어 방식 전환을 이벤트 파일에 쓴다. 바뀐 방식은 다음 상자부터 적용된다.
    /// [호출] CellController.SetMode() (예약에 성공했을 때)
    public void WriteModeChange(ControlMode mode)
    {
        WriteEvent("제어 방식 전환", "", $"{mode} (다음 상자부터)");
    }

    // 세션 지표 요약 1줄을 이벤트 파일에 쓴다.
    /// [호출] OnDestroy() (Play 종료)
    /// [!] Play 종료 때는 CellController가 먼저 지워질 수 있다. Unity의 == null은 지워진 컴포넌트를 null로 보므로
    ///     ReferenceEquals로 확인한다 (집계 값은 C# 값이라 그대로 읽힌다).
    public void WriteSummary(CellController source)
    {
        if (!Open()) { return; }

        string text = "";
        if (!ReferenceEquals(source, null))
        {
            string reasons = "";
            for (int i = 1; i < source.ExceptionsByReason.Length; i++)
                if (source.ExceptionsByReason[i] > 0) { reasons += $" {(ExceptionReason)i} {source.ExceptionsByReason[i]}"; }
            text = $"처리 {source.Processed} | 성공 {source.Success} · 실패 {source.Fail} · 예외 {source.Exceptions}" +
                   (reasons == "" ? "" : $" ({reasons.Trim()})") +
                   $" | A {source.TerminalCounts[0]} · B {source.TerminalCounts[1]} · C {source.TerminalCounts[2]}" +
                   $" | 예외율 {source.ExceptionRate * 100f:0.#}% | 시간당 {PerHour(source.Processed):0}개 | ";
        }
        text += $"{ModeText(ControlMode.Basic)} | {ModeText(ControlMode.Policy)}";

        WriteEvent("세션 요약", "", text);
        Debug.Log($"[Log] 세션 요약 | {text}", this);
    }


    //  ============================================================
    //  파일
    //  ============================================================
    // 두 파일을 만들고 제목 줄을 쓴다. 이미 열려 있으면 그대로 true, 닫은 뒤면 false.
    bool Open()
    {
        if (closed) { return false; }
        if (boxWriter != null) { return true; }

        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), folderName);    //  dataPath = 프로젝트/Assets --> 그 부모 = 프로젝트 폴더
        Directory.CreateDirectory(folder);                                                          //  폴더가 없으면 만든다
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        BoxFilePath = Path.Combine(folder, $"boxes_{stamp}.csv");
        EventFilePath = Path.Combine(folder, $"events_{stamp}.csv");

        boxWriter = NewWriter(BoxFilePath);
        eventWriter = NewWriter(EventFilePath);
        boxWriter.WriteLine("상자 ID,생성(초),초기 면,비율 위,비율 앞,비율 뒤,비율 왼쪽,비율 오른쪽,규칙 판정,제어 방식,회전 횟수,뒤집기,마지막 회전," +
                            "송장 찾음,판독 번호,판독 지역,모름 칸,재시도,검증,지역 코드,목적지,실제 도착,결과,사유,정답 번호,정답 지역," +
                            "투입→교정(초),교정→판독(초),판독→완료(초),전체(초)");
        eventWriter.WriteLine("시각(초),시계,종류,상자 ID,내용");
        boxWriter.Flush();
        eventWriter.Flush();

        Debug.Log($"[Log] 로그 파일 만듦 | {BoxFilePath}", this);
        return true;
    }

    // 파일 쓰기 도구. false = 덮어쓰기, UTF8Encoding(true) = BOM을 붙여 엑셀에서 한글이 깨지지 않게
    static StreamWriter NewWriter(string path)
    {
        return new StreamWriter(path, false, new UTF8Encoding(true));
    }

    // 칸을 쉼표로 이어 1줄 쓰고 바로 파일에 반영한다.
    static void WriteLine(StreamWriter writer, string[] columns)
    {
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i] = Csv(columns[i]);
        }
        writer.WriteLine(string.Join(",", columns));
        writer.Flush();
    }

    // 파일을 닫는다. 닫은 뒤에는 더 쓰지 않는다.
    void Close()
    {
        closed = true;
        if (boxWriter != null) { boxWriter.Close(); }
        if (eventWriter != null) { eventWriter.Close(); }
        boxWriter = null;
        eventWriter = null;
    }


    //  ============================================================
    //  집계 (세션 요약용)
    //  ============================================================
    // 상자 1개를 세션 요약 집계에 더한다.
    /// 교정 성공 = 송장이 붙은 상자 중 사유가 NoLabel · JudgeFail · CorrectionFail이 아닌 상자 (판독 스테이션에서 송장이 윗면에 있었음).
    void AddToSummary(BoxInfo box)
    {
        if (box.createdTime > 0f && (firstCreated < 0f || box.createdTime < firstCreated)) { firstCreated = box.createdTime; }
        if (box.finishTime > lastFinished) { lastFinished = box.finishTime; }

        int m = (int)box.mode;
        if (m < 0 || m >= modeBoxes.Length || !box.hasLabel) { return; }

        modeBoxes[m]++;
        modeRotations[m] += box.rotationCount;
        bool corrected = box.reason != ExceptionReason.NoLabel && box.reason != ExceptionReason.JudgeFail && box.reason != ExceptionReason.CorrectionFail;
        if (corrected) { modeCorrected[m]++; }
        if (box.correctArriveTime > 0f && box.readArriveTime > box.correctArriveTime)
        {
            modeCorrectSeconds[m] += box.readArriveTime - box.correctArriveTime;
            modeTimed[m]++;
        }
    }

    // 제어 방식 1개의 요약 글자 (교정 성공률, 평균 회전, 평균 교정 → 판독 시간)
    string ModeText(ControlMode mode)
    {
        int m = (int)mode;
        if (modeBoxes[m] == 0) { return $"{mode}: 상자 없음"; }

        float rate = 100f * modeCorrected[m] / modeBoxes[m];
        float rotations = (float)modeRotations[m] / modeBoxes[m];
        string seconds = modeTimed[m] > 0 ? F(modeCorrectSeconds[m] / modeTimed[m]) : "-";
        return $"{mode}: 교정 성공 {modeCorrected[m]}/{modeBoxes[m]} ({rate:0.#}%) · 평균 회전 {rotations:0.00} · 평균 교정 {seconds}초";
    }

    // 시간당 처리 수 (첫 투입 ~ 마지막 완료 사이 평균). 잴 수 없으면 0
    float PerHour(int processed)
    {
        float seconds = lastFinished - Mathf.Max(0f, firstCreated);
        return seconds > 0f ? processed * 3600f / seconds : 0f;
    }


    //  ============================================================
    //  글자 만들기
    //  ============================================================
    // 소수 첫째 자리. InvariantCulture: 나라 설정과 상관없이 소수점을 점(.)으로 (쉼표가 되면 CSV 칸이 갈라짐)
    static string F(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    // 카메라 비율 i번 (0 위, 1 앞, 2 뒤, 3 왼쪽, 4 오른쪽). 배열이 짧으면 빈 칸
    static string Ratio(BoxInfo box, int i)
    {
        if (box.faceRatios == null || i >= box.faceRatios.Length) { return ""; }
        return box.faceRatios[i].ToString("0.000", CultureInfo.InvariantCulture);
    }

    // 구간 시간 (to - from, 초). 두 시각 중 하나라도 기록되지 않았으면(0) 빈 칸
    static string Span(float from, float to)
    {
        if (to <= 0f || to < from) { return ""; }
        return F(to - from);
    }

    // CSV 칸 1개. 쉼표 · 따옴표 · 줄바꿈이 있으면 따옴표로 감싸고, 안의 따옴표는 두 번 쓴다
    static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value)) { return ""; }
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) { return value; }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }


    //  ============================================================
    //  테스트 (ProductionLog ⋮ 메뉴)
    //  ============================================================
    [ContextMenu("테스트/로그 파일 경로 출력")]
    void TestPrintPath()
    {
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), folderName);
        string files = boxWriter != null ? $"{BoxFilePath} | {EventFilePath}" : "아직 만들지 않음 (처음 기록할 때 만든다)";
        Debug.Log($"[Log] 폴더 {folder} | 파일 {files} | 상자 {BoxLines}줄 · 이벤트 {EventLines}줄", this);
    }
}
