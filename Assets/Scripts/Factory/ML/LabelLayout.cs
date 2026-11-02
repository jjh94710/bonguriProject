using TMPro;
using UnityEngine;

/// <summary>
/// 송장 한 장의 배치를 한 에셋에 모은다. 글꼴, 숫자 칸 5 개와 지역명 칸 위치, 글자 크기다.
///
/// BoxFeeder 가 송장을 찍을 때와 LabelReaderAgent · LabelTrainer 가 읽을 때 같은 값을
/// 써야 찍는 위치와 읽는 위치가 어긋나지 않는다. 그래서 MonoBehaviour 가 아니라 에셋이다
/// (클래스 설계서 LabelLayout, 학습 설계안 v3).
///
/// 칸 위치는 SRS v1.5 부록 A.2 "송장 배치"를 따른다.
///   바코드는 왼쪽 위, 번호 5 칸은 오른쪽 위, 주소는 아래쪽.
///   송장은 한 면 가운데 3.6 × 4.5 cm.
///
/// [주의] 글꼴 · 칸 위치 · 크기를 바꾸면 LabelReaderAgent 를 다시 학습해야 한다.
///        학습을 시작하기 전에 확정한다 (A.2, 설계서 7 장).
/// </summary>
[CreateAssetMenu(fileName = "LabelLayout", menuName = "Factory/Label Layout")]
public class LabelLayout : ScriptableObject
{
    public const int DigitCount = 5;

    [Header("글꼴")]
    [Tooltip("송장 글꼴. TextMesh 는 비트맵이라 작게 줄이면 뭉개진다. TMP 는 SDF 라 유지된다.")]
    public TMP_FontAsset font;

    [Tooltip("글자 크기의 시작값. 칸은 5 mm 쯤이므로 TMP 기본값 36 을 넣으면 송장을 덮는다. "
           + "Print 가 실제로 그려 보고 칸 높이에 맞게 다시 잡으므로 대충 둬도 된다.")]
    public float fontSize = .005f;

    [Tooltip("글자가 칸 높이를 채우는 비율. 1 이면 칸에 꽉 찬다.")]
    [Range(.5f, 1f)] public float fillRatio = .9f;

    [Tooltip("굵게. Light 글꼴의 얇은 획을 SDF 팽창으로 두껍게 만든다. 32 픽셀로 읽을 때 획이 끊기지 않게 한다.")]
    public bool bold = true;

    [Header("송장")]
    [Tooltip("송장 크기 (m). 부록 A.2 는 3.6 × 4.5 cm 다.")]
    public Vector2 labelSize = new Vector2(.036f, .045f);

    [Header("칸 위치 (송장 로컬 0 ~ 1)")]
    // 칸은 실제 길이로 정사각형이어야 한다. 32 x 32 로 찍을 때 세로 시야와 가로 시야가
    // 같아지는데, 칸이 세로로 길면 가로가 남아 옆 칸 숫자가 같이 들어온다.
    //   가로 .075 x 36 mm = 2.70 mm  →  세로도 2.70 mm 가 되려면 2.70 / 45 = .06
    [Tooltip("숫자 칸 5 개. A.2 의 '번호 5칸은 오른쪽 위'다. 실제 길이로 정사각형이어야 한다.")]
    public Rect[] digitCells =
    {
        new Rect(.5275f, .80f, .075f, .06f),
        new Rect(.6175f, .80f, .075f, .06f),
        new Rect(.7075f, .80f, .075f, .06f),
        new Rect(.7975f, .80f, .075f, .06f),
        new Rect(.8875f, .80f, .075f, .06f),
    };

    [Tooltip("주소 첫 단어(지역명) 칸. A.2 의 '주소는 아래쪽'이다.")]
    public Rect regionCell = new Rect(.045f, .1875f, .35f, .105f);

    // 판독은 바코드를 읽지 않는다(설계안 4.1 이 Align 을 없앴다). 그래도 A.2 가 자리를
    // 정해 두었고 번호 칸과 겹치면 안 되므로 같은 에셋에서 관리한다.
    [Tooltip("바코드 칸. A.2 의 '바코드는 왼쪽 위'다. 판독은 읽지 않는다.")]
    public Rect barcodeCell = new Rect(.03f, .77f, .42f, .12f);

    [Header("판독 카메라")]
    [Tooltip("판독 카메라가 칸 하나를 찍는 해상도 (픽셀). 설계안 4.2 의 숫자 카메라 32 x 32 다. "
           + "LabelReaderAgent 의 CameraSensor 와 같은 값이어야 한다.")]
    [Min(8)] public int readPixels = 32;

    // 여백을 키우면 칸이 화면에서 작아진다. 설계안 4.2 가 "칸당 28 픽셀 이상"을 요구하므로
    // 32 픽셀에서 쓸 수 있는 여백은 32/28 - 1 = 14.3 % 까지다. 기본값은 조금 여유를 둔다.
    [Tooltip("칸 둘레 여백 비율. Orthographic Size 가 칸 절반 × (1 + 이 값) 이 된다.")]
    [Min(0f)] public float camMargin = .12f;

    // 설계안은 ±1 mm 였지만 그건 상자가 6 cm 정육면체이던 때 값이다. 지금 칸은 2.70 mm 라
    // 창(3.02 mm)과 글자(2.43 mm) 사이 여유가 세로로 ±0.30 mm 뿐이다. ±1 mm 로 흔들면
    // 목표 숫자의 위아래가 잘린 그림이 학습에 섞인다.
    [Tooltip("학습 때 카메라를 흔드는 범위 (m). 모름 칸 재판독에도 쓴다.")]
    [Min(0f)] public float jitterPos = .0002f;

    [Tooltip("학습 때 카메라를 비트는 범위 (도).")]
    [Min(0f)] public float jitterAngle = 3f;

    // 글자 크기와 칸 보정값은 한 번 풀면 바뀌지 않는다. 송장마다 다시 재면
    // 학습 중에 ForceMeshUpdate 가 쌓여 느려지므로 캐시한다.
    [System.NonSerialized] float solvedDigitSize, solvedRegionSize;

    // 마지막으로 그린 번호가 송장에서 차지한 자리(읽는 좌표, m). 자리마다 조준하지
    // 않고 번호 전체를 한 장에 담을 때, 창을 글자에 딱 맞추는 데 쓴다.

    /// <summary>
    /// 글꼴 지표로 푼 크기에 곱하는 값. TMP 가 실제로 그리는 높이는 글꼴 지표만으로는
    /// 정확히 안 나와서(패딩 · 내부 배율), 찍는 쪽이 한 장 찍어 재고 여기에 넣는다.
    /// 학습과 운전에서 같은 크기를 쓰도록 보정값을 에셋에 저장한다.
    /// </summary>
    [Min(.0001f)] public float sizeTweak = 1f;

    // Print 가 글자를 놓을 때 쓴 송장 평면의 가로 · 세로 축 (송장 로컬).
    // Aim 이 같은 축을 써야 카메라가 글자 위에 선다. 두 곳이 어긋나면 카메라가
    // 송장의 정반대 자리를 찍는데, 거기도 종이라서 빈 그림이 조용히 쌓인다.
    [System.NonSerialized] Vector3 textRight = Vector3.right;
    [System.NonSerialized] Vector3 textUp = Vector3.forward;

    // ── 칸 좌표 ─────────────────────────────────────────────────

    /// <summary>번호 5 자리를 한 장에 찍을 때 쓰는 칸 번호.</summary>
    public const int NumberBlockCell = -1;

    /// <summary>
    /// 숫자 칸 5 개를 모두 덮는 네모. 번호를 통째로 찍으면 자리마다 조준할 필요가 없고,
    /// 가로로 길게 찍으므로 자리당 해상도는 칸 하나를 찍을 때와 같다.
    /// </summary>
    public Rect NumberBlock
    {
        get
        {
            if (digitCells == null || digitCells.Length == 0) return regionCell;

            Rect r = digitCells[0];
            foreach (Rect c in digitCells)
                r = Rect.MinMaxRect(Mathf.Min(r.xMin, c.xMin), Mathf.Min(r.yMin, c.yMin),
                                    Mathf.Max(r.xMax, c.xMax), Mathf.Max(r.yMax, c.yMax));
            return r;
        }
    }

    public Rect CellOf(int index) =>
        index == NumberBlockCell ? NumberBlock
        : index >= 0 && digitCells != null && index < digitCells.Length ? digitCells[index]
        : regionCell;

    /// <summary>칸 가운데를 송장 로컬 미터 좌표로. 송장 가운데가 (0, 0) 이다.</summary>
    public Vector2 CellCenterMetres(int index)
    {
        // 창은 설계상의 네모로 고정한다. 그려진 글자에 맞추면, 글자 크기가 틀어졌을 때
        // 창이 같이 밀려 바코드 쪽을 보게 되고 크기 맞추기가 바코드를 재는 악순환이 된다.
        Rect r = CellOf(index);
        return new Vector2((r.center.x - .5f) * labelSize.x, (r.center.y - .5f) * labelSize.y);
    }

    /// <summary>칸 크기를 미터로.</summary>
    /// <summary>
    /// 찍을 창의 크기. 번호 전체도 설계상의 네모를 쓴다. 그려진 글자에 맞추면 번호마다
    /// 창이 달라져 그림 크기가 제각각이 되는데, 학습 입력은 크기가 고정이어야 한다.
    /// 자리는 CellCenterMetres 가 그려진 글자에 맞추므로 번호는 늘 가운데에 온다.
    /// </summary>
    public Vector2 CellSizeMetres(int index)
    {
        Rect r = CellOf(index);
        return new Vector2(r.width * labelSize.x, r.height * labelSize.y);
    }

    // ── 찍기 (BoxFeeder, LabelTrainer) ──────────────────────────

    /// <summary>
    /// numberText · regionText 에 글자를 칸 위치대로 찍는다.
    /// 숫자는 TMP 의 &lt;pos&gt; 로 칸마다 가로 위치를 직접 지정하고,
    /// 찍은 뒤 실제 글자 위치를 재서 한 번 보정한다. 글꼴이 바뀌어도 칸을 벗어나지 않는다.
    /// </summary>
    public void Print(BoxInfo box, string number, string region)
    {
        if (box == null) return;

        box.labelNumber = number;
        box.labelRegion = region;

        PrintNumber(box.numberText, number);
        PrintRegion(box.regionText, region);
    }

    void PrintNumber(TextMeshPro text, string number)
    {
        if (text == null || string.IsNullOrEmpty(number)) return;

        // 번호는 통째로 읽으므로 자리마다 가로 위치를 지정하지 않는다. TMP 가 글꼴
        // 간격대로 그리게 두면 자리 간격이 고르고, 찍는 창은 그려진 글자에 맞추므로
        // 바코드나 종이 밖이 끼어들지 않는다 (AimAtNumber 가 DrawnNumber 를 쓴다).
        Apply(text, NumberBlock, ref solvedDigitSize,
              digitCells[0].height * labelSize.y, number);

        text.alignment = TextAlignmentOptions.Center;
        text.text = number;
        text.ForceMeshUpdate();
    }

    void PrintRegion(TextMeshPro text, string region)
    {
        if (text == null) return;
        Apply(text, regionCell, ref solvedRegionSize, regionCell.height * labelSize.y,
              string.IsNullOrEmpty(region) ? "서울" : region);
        text.alignment = TextAlignmentOptions.Center;
        text.text = region ?? string.Empty;
        text.ForceMeshUpdate();
    }

    /// <summary>
    /// 글자판을 칸 위에 놓고 글꼴과 크기를 맞춘다.
    /// 글자 크기는 한 번 그려 보고 칸 높이에 맞게 풀어 캐시한다. TMP 의 fontSize 는
    /// 글꼴과 오브젝트 배율에 따라 뜻이 달라져서 숫자를 미리 못 박는다.
    /// </summary>
    void Apply(TextMeshPro text, Rect cell, ref float solved, float targetHeight, string sample)
    {
        if (font != null) text.font = font;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.color = Color.black;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.enableAutoSizing = false;

        var rt = text.rectTransform;

        // 종이 위로 띄운 높이는 글자판을 처음 둘 때 정해졌다. 먼저 읽어 둔다.
        float lift = rt.anchoredPosition3D.y;

        rt.sizeDelta = new Vector2(cell.width * labelSize.x, cell.height * labelSize.y);

        // 송장은 부모의 XZ 평면에 누워 있고 글자는 그 위를 향한다. 칸 좌표(0~1)는
        // 글자가 읽히는 방향 기준이라, 글자 자신의 회전으로 가로 · 세로 축을 꺼내
        // 거기에 실어야 한다. 이 상자들에서는 읽는 오른쪽이 로컬 -X, 위쪽이 -Z 다.
        Vector3 right = rt.localRotation * Vector3.right;
        Vector3 up = rt.localRotation * Vector3.up;

        // Aim 이 그대로 쓴다. 글자를 놓은 축과 카메라가 겨누는 축은 반드시 같아야 한다.
        textRight = right;
        textUp = up;

        Vector3 offset = right * ((cell.center.x - .5f) * labelSize.x)
                       + up * ((cell.center.y - .5f) * labelSize.y);

        // RectTransform 은 localPosition 의 x · y 를 anchoredPosition 이 몬다. localPosition
        // 에 직접 넣으면 레이아웃이 다시 계산될 때 (0, 0) 으로 되돌아가고 z 만 남는다.
        rt.anchoredPosition3D = new Vector3(offset.x, lift, offset.z);

        if (solved <= 0f) solved = SolveSize(text, targetHeight, sample);
        text.fontSize = solved * Mathf.Max(.01f, sizeTweak);
    }

    /// <summary>
    /// 글자 하나를 그려 보고, 그 높이가 칸 높이 × fillRatio 가 되는 fontSize 를 돌려준다.
    /// TMP 가 그리는 높이는 fontSize 에 비례하므로 한 번 재면 정확히 맞는다.
    /// 줄 높이가 아니라 글자 네모의 높이를 재야 숫자가 칸에 꽉 찬다.
    /// </summary>
    float SolveSize(TextMeshPro text, float targetHeight, string sample)
    {
        float want = targetHeight * fillRatio;

        // 글꼴이 알려 주는 대문자 높이를 쓴다. characterInfo 네모도 mesh.bounds 도
        // 글자 둘레 여백과 SDF 패딩을 포함한 상자라, 그걸 칸에 맞추면 보이는 글자가
        // 절반쯤으로 작아진다. capLine 은 숫자가 실제로 차지하는 높이다.
        var face = text.font != null ? text.font.faceInfo : default;
        float cap = face.pointSize > 0f ? face.capLine / face.pointSize : 0f;

        if (cap < 1e-4f)
        {
            Debug.LogWarning($"[LabelLayout] 글꼴에서 대문자 높이를 못 읽었다 "
                           + $"(capLine {face.capLine}, pointSize {face.pointSize}). "
                           + "글자 크기를 시작값으로 둔다.", this);
            return Mathf.Max(.0001f, fontSize);
        }

        return want / cap;
    }

    // ── 읽기 (LabelReaderAgent) ─────────────────────────────────

    /// <summary>숫자 칸 위에 카메라를 놓고 Orthographic Size 를 맞춘다.</summary>
    public void AimAtDigit(Camera cam, Transform label, int index, bool jitter) =>
        Aim(cam, label, Mathf.Clamp(index, 0, DigitCount - 1), jitter);

    /// <summary>지역명 칸 위에 카메라를 놓는다.</summary>
    public void AimAtRegion(Camera cam, Transform label, bool jitter) =>
        Aim(cam, label, DigitCount, jitter);

    /// <summary>
    /// 번호 5 자리 전체 위에 카메라를 놓는다. 자리마다 조준하지 않으므로 칸이 어긋나
    /// 라벨과 그림이 안 맞는 일이 없다. 그림은 가로로 길게 찍어야 한다
    /// (가로세로비는 NumberBlockAspect).
    /// </summary>
    public void AimAtNumber(Camera cam, Transform label, bool jitter) =>
        Aim(cam, label, NumberBlockCell, jitter);

    /// <summary>번호 블록을 찍을 그림의 가로세로비. 세로 32 px 이면 가로는 이 값 × 32 다.</summary>
    public float NumberBlockAspect
    {
        get
        {
            Vector2 s = CellSizeMetres(NumberBlockCell);
            return s.y > 1e-9f ? s.x / s.y : 1f;
        }
    }

    /// <summary>
    /// 카메라를 송장 좌표의 칸 위에 놓는다. 송장이 0 · 90 · 180 · 270 도 돌아가 있어도
    /// 카메라가 같이 돌아가므로 바코드로 방향을 찾는 단계가 필요 없다 (설계안 4.1).
    /// </summary>
    void Aim(Camera cam, Transform label, int index, bool jitter)
    {
        if (cam == null || label == null) return;

        Vector2 centre = CellCenterMetres(index);
        Vector2 size = CellSizeMetres(index);

        // 칸 좌표(0~1)는 글자가 읽히는 방향 기준이다. Print 가 글자를 놓을 때 쓴 바로 그
        // 가로 · 세로 축에 실어야 카메라가 글자 위에 선다. 라벨의 X · Z 를 그대로 쓰면
        // 이 상자들에서는 읽는 오른쪽이 -X, 위쪽이 -Z 라 정반대 자리를 겨눈다.
        Vector3 planeRight = textRight, planeUp = textUp;

        Vector3 local = planeRight * centre.x + planeUp * centre.y;
        Vector3 normal = label.up;

        if (jitter && jitterPos > 0f)
            local += planeRight * Random.Range(-jitterPos, jitterPos)
                   + planeUp * Random.Range(-jitterPos, jitterPos);

        // 송장이 상자에 축소돼 붙어 있으면(ParcelBox 는 0.583 배) 칸 좌표는 송장 로컬이라
        // 같이 줄어드는데, 카메라의 시야와 거리는 월드 길이다. 스케일을 곱하지 않으면
        // 창만 1/0.583 = 1.7 배 넓게 열려 바코드와 종이 밖이 같이 들어온다.
        float scale = Mathf.Abs(label.lossyScale.x);
        if (scale < 1e-6f) scale = 1f;

        float distance = Mathf.Max(.01f, size.y * 4f) * scale;
        cam.transform.position = label.TransformPoint(local) + normal * distance;

        // 카메라의 위쪽을 글자의 위쪽에 맞춰야 숫자가 바로 선 채로 찍힌다.
        cam.transform.rotation = Quaternion.LookRotation(-normal, label.TransformDirection(planeUp));
        Vector3 up = normal;

        if (jitter && jitterAngle > 0f)
            cam.transform.rotation = Quaternion.AngleAxis(
                Random.Range(-jitterAngle, jitterAngle), up) * cam.transform.rotation;

        cam.orthographic = true;
        cam.orthographicSize = size.y * .5f * (1f + camMargin) * scale;

        // 가로 비율은 찍는 그림(32 x 32, 96 x 32)이 정한다. 여기서 칸 비율로 고정하면
        // 세로로 긴 숫자 칸이 정사각형 그림에 눌려 담겨 글자가 가로로 늘어난다.
        cam.ResetAspect();
        cam.nearClipPlane = .001f;
        cam.farClipPlane = distance + size.y * scale;
    }

    // ── 검산 ────────────────────────────────────────────────────

    /// <summary>
    /// 칸 위치와 크기를 SRS 부록 A.2 기본값으로 되돌린다.
    /// 에셋에 한 번 저장된 값은 스크립트 기본값을 고쳐도 안 바뀌므로, 배치를 손본 뒤에는
    /// 에셋에서 이걸 한 번 눌러야 한다 (인스펙터 ⋮ 메뉴).
    /// </summary>
    [ContextMenu("A.2 기본 배치로 되돌리기")]
    public void ResetToA2()
    {
        labelSize = new Vector2(.036f, .045f);

        digitCells = new[]
        {
            new Rect(.5275f, .80f, .075f, .06f),
            new Rect(.6175f, .80f, .075f, .06f),
            new Rect(.7075f, .80f, .075f, .06f),
            new Rect(.7975f, .80f, .075f, .06f),
            new Rect(.8875f, .80f, .075f, .06f),
        };
        regionCell = new Rect(.045f, .1875f, .35f, .105f);
        barcodeCell = new Rect(.03f, .77f, .42f, .12f);

        readPixels = 32;
        camMargin = .12f;
        fillRatio = .9f;
        jitterPos = .0002f;
        jitterAngle = 3f;

        solvedDigitSize = solvedRegionSize = 0f;

#if UNITY_EDITOR
        // SetDirty 만으로는 메모리에서만 바뀐다. 디스크까지 써야 Unity 를 다시 켜도 남는다.
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
#endif
        Debug.Log("[LabelLayout] 칸 위치를 A.2 기본값으로 되돌렸다. "
                + "숫자 칸은 2.70 x 2.70 mm 정사각형이다.", this);
    }

    void OnValidate()
    {
        // 값을 바꿨으면 풀어 둔 글자 크기와 칸 보정값을 버리고 다시 풀게 한다.
        solvedDigitSize = solvedRegionSize = 0f;

        if (digitCells == null || digitCells.Length != DigitCount)
        {
            Debug.LogWarning($"[LabelLayout] 숫자 칸이 {DigitCount} 개여야 한다.", this);
            return;
        }

        for (int i = 0; i < digitCells.Length; i++)
        {
            Rect r = digitCells[i];
            if (r.xMin < 0f || r.yMin < 0f || r.xMax > 1f || r.yMax > 1f)
                Debug.LogWarning($"[LabelLayout] 숫자 칸 {i} 가 송장을 벗어난다: {r}", this);
            if (i > 0 && digitCells[i].xMin < digitCells[i - 1].xMax - 1e-4f)
                Debug.LogWarning($"[LabelLayout] 숫자 칸 {i - 1} 과 {i} 가 겹친다.", this);
        }

        if (regionCell.xMin < 0f || regionCell.yMin < 0f || regionCell.xMax > 1f || regionCell.yMax > 1f)
            Debug.LogWarning($"[LabelLayout] 지역명 칸이 송장을 벗어난다: {regionCell}", this);

        // 설계안 4.2 는 칸당 28 픽셀 이상을 요구한다.
        float pixels = readPixels / (1f + camMargin);
        if (pixels < 28f)
        {
            float allowed = readPixels / 28f - 1f;
            Debug.LogWarning($"[LabelLayout] 칸이 {readPixels} 픽셀 중 {pixels:F1} 픽셀만 차지한다. "
                           + $"설계안 4.2 는 28 픽셀 이상을 요구하므로 여백을 {allowed:P0} 이하로 "
                           + $"줄이거나 판독 해상도를 올린다 (지금 여백 {camMargin:P0}).", this);
        }
    }
}
