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
    [Tooltip("숫자 칸 5 개. A.2 의 '번호 5칸은 오른쪽 위'다.")]
    public Rect[] digitCells =
    {
        new Rect(.5275f, .775f, .075f, .11f),
        new Rect(.6175f, .775f, .075f, .11f),
        new Rect(.7075f, .775f, .075f, .11f),
        new Rect(.7975f, .775f, .075f, .11f),
        new Rect(.8875f, .775f, .075f, .11f),
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

    [Tooltip("학습 때 카메라를 흔드는 범위 (m). 모름 칸 재판독에도 쓴다.")]
    [Min(0f)] public float jitterPos = .001f;

    [Tooltip("학습 때 카메라를 비트는 범위 (도).")]
    [Min(0f)] public float jitterAngle = 3f;

    // 글자 크기와 칸 보정값은 한 번 풀면 바뀌지 않는다. 송장마다 다시 재면
    // 학습 중에 ForceMeshUpdate 가 쌓여 느려지므로 캐시한다.
    [System.NonSerialized] float solvedDigitSize, solvedRegionSize;
    [System.NonSerialized] float[] solvedOffsets;

    // ── 칸 좌표 ─────────────────────────────────────────────────

    public Rect CellOf(int index) =>
        index >= 0 && digitCells != null && index < digitCells.Length ? digitCells[index] : regionCell;

    /// <summary>칸 가운데를 송장 로컬 미터 좌표로. 송장 가운데가 (0, 0) 이다.</summary>
    public Vector2 CellCenterMetres(int index)
    {
        Rect r = CellOf(index);
        return new Vector2((r.center.x - .5f) * labelSize.x, (r.center.y - .5f) * labelSize.y);
    }

    /// <summary>칸 크기를 미터로.</summary>
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

        // 글자판은 숫자 칸 다섯 개를 다 덮는 사각형이다.
        Rect first = digitCells[0], last = digitCells[digitCells.Length - 1];
        float left = first.xMin, right = last.xMax;
        var block = Rect.MinMaxRect(left, first.yMin, right, first.yMax);

        Apply(text, block, ref solvedDigitSize, first.height * labelSize.y, number);
        text.alignment = TextAlignmentOptions.TopLeft;

        float blockWidth = block.width;

        if (solvedOffsets == null || solvedOffsets.Length != number.Length)
        {
            // 1 차: 칸 왼쪽 끝에 글자를 놓는다.
            var offsets = new float[number.Length];
            for (int i = 0; i < number.Length && i < digitCells.Length; i++)
                offsets[i] = (digitCells[i].xMin - left) / blockWidth;

            text.text = Compose(number, offsets);
            text.ForceMeshUpdate();

            // 2 차: 글자 가운데가 칸 가운데에 오도록 한 번 민다. 글자 폭은 자리마다
            // 거의 같으므로 한 번 민 값을 다음 송장에도 그대로 쓴다.
            var info = text.textInfo;
            var rect = text.rectTransform.rect;
            for (int i = 0; i < number.Length && i < digitCells.Length; i++)
            {
                if (i >= info.characterCount || !info.characterInfo[i].isVisible) continue;

                var ch = info.characterInfo[i];
                float centre = (ch.bottomLeft.x + ch.topRight.x) * .5f;       // 글자판 로컬
                float want = (digitCells[i].center.x - left) / blockWidth;     // 0 ~ 1
                float have = (centre - rect.xMin) / rect.width;
                offsets[i] += want - have;
            }
            solvedOffsets = offsets;
        }

        text.text = Compose(number, solvedOffsets);
        text.ForceMeshUpdate();
    }

    static string Compose(string number, float[] offsets)
    {
        var sb = new System.Text.StringBuilder(number.Length * 16);
        for (int i = 0; i < number.Length; i++)
        {
            float pos = i < offsets.Length ? Mathf.Clamp01(offsets[i]) : 0f;
            sb.Append("<pos=").Append((pos * 100f).ToString("F3",
                System.Globalization.CultureInfo.InvariantCulture)).Append("%>");
            sb.Append(number[i]);
        }
        return sb.ToString();
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
        Vector3 offset = right * ((cell.center.x - .5f) * labelSize.x)
                       + up * ((cell.center.y - .5f) * labelSize.y);

        // RectTransform 은 localPosition 의 x · y 를 anchoredPosition 이 몬다. localPosition
        // 에 직접 넣으면 레이아웃이 다시 계산될 때 (0, 0) 으로 되돌아가고 z 만 남는다.
        rt.anchoredPosition3D = new Vector3(offset.x, lift, offset.z);

        if (solved <= 0f) solved = SolveSize(text, targetHeight, sample);
        text.fontSize = solved;
    }

    /// <summary>
    /// 글자 하나를 그려 보고, 그 높이가 칸 높이 × fillRatio 가 되는 fontSize 를 돌려준다.
    /// TMP 가 그리는 높이는 fontSize 에 비례하므로 한 번 재면 정확히 맞는다.
    /// 줄 높이가 아니라 글자 네모의 높이를 재야 숫자가 칸에 꽉 찬다.
    /// </summary>
    float SolveSize(TextMeshPro text, float targetHeight, string sample)
    {
        float start = Mathf.Max(.0001f, fontSize);
        text.fontSize = start;
        text.text = sample;
        text.ForceMeshUpdate();

        var info = text.textInfo;
        float measured = 0f;
        for (int i = 0; i < info.characterCount; i++)
        {
            if (!info.characterInfo[i].isVisible) continue;
            var ch = info.characterInfo[i];
            measured = Mathf.Max(measured, ch.topRight.y - ch.bottomLeft.y);
        }

        if (measured < 1e-9f)
        {
            Debug.LogWarning($"[LabelLayout] '{sample}' 을 그리지 못했다. 글꼴 아틀라스에 "
                           + "그 글자가 없을 수 있다.", this);
            return start;
        }

        return start * (targetHeight * fillRatio) / measured;
    }

    // ── 읽기 (LabelReaderAgent) ─────────────────────────────────

    /// <summary>숫자 칸 위에 카메라를 놓고 Orthographic Size 를 맞춘다.</summary>
    public void AimAtDigit(Camera cam, Transform label, int index, bool jitter) =>
        Aim(cam, label, Mathf.Clamp(index, 0, DigitCount - 1), jitter);

    /// <summary>지역명 칸 위에 카메라를 놓는다.</summary>
    public void AimAtRegion(Camera cam, Transform label, bool jitter) =>
        Aim(cam, label, DigitCount, jitter);

    /// <summary>
    /// 카메라를 송장 좌표의 칸 위에 놓는다. 송장이 0 · 90 · 180 · 270 도 돌아가 있어도
    /// 카메라가 같이 돌아가므로 바코드로 방향을 찾는 단계가 필요 없다 (설계안 4.1).
    /// </summary>
    void Aim(Camera cam, Transform label, int index, bool jitter)
    {
        if (cam == null || label == null) return;

        Vector2 centre = CellCenterMetres(index);
        Vector2 size = CellSizeMetres(index);

        // 송장 로컬에서 글자가 누운 평면은 XZ 이고, 송장이 보는 쪽이 +Y 다.
        Vector3 local = new Vector3(centre.x, 0f, centre.y);
        Vector3 up = label.up;
        Vector3 right = label.right;

        if (jitter && jitterPos > 0f)
            local += new Vector3(Random.Range(-jitterPos, jitterPos), 0f,
                                 Random.Range(-jitterPos, jitterPos));

        float distance = Mathf.Max(.01f, size.y * 4f);
        cam.transform.position = label.TransformPoint(local) + up * distance;
        cam.transform.rotation = Quaternion.LookRotation(-up, right);

        if (jitter && jitterAngle > 0f)
            cam.transform.rotation = Quaternion.AngleAxis(
                Random.Range(-jitterAngle, jitterAngle), up) * cam.transform.rotation;

        cam.orthographic = true;
        cam.orthographicSize = size.y * .5f * (1f + camMargin);
        cam.aspect = Mathf.Max(.0001f, size.x / size.y);
        cam.nearClipPlane = .001f;
        cam.farClipPlane = distance + size.y;
    }

    // ── 검산 ────────────────────────────────────────────────────

    void OnValidate()
    {
        // 값을 바꿨으면 풀어 둔 글자 크기와 칸 보정값을 버리고 다시 풀게 한다.
        solvedDigitSize = solvedRegionSize = 0f;
        solvedOffsets = null;

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
