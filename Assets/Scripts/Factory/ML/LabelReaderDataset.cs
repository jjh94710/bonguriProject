using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 판독 학습·평가용 칸 이미지를 만든다 (FR-04, 학습 설계안 4.2).
///
/// 송장을 찍는 일은 BoxFeeder 와 똑같이 LabelLayout.Print 가 하고, 칸 위에 카메라를
/// 놓는 일도 LabelLayout.AimAtDigit / AimAtRegion 이 한다. 그래서 여기서 만든
/// 이미지는 공정 씬에서 판독이 보게 될 그림과 같다. 둘이 다르면 학습이 쓸모없어진다.
///
/// 숫자 칸은 readPixels 정사각형, 지역명 칸은 그 3 배 너비로 찍는다
/// (설계안 4.2: 숫자 32 x 32, 지역명 96 x 32 회색조).
///
/// [주의] LabelLayout 의 글꼴 · 칸 위치 · 크기를 바꾸면 이 데이터셋을 다시 만들어야 한다.
/// </summary>
[DisallowMultipleComponent]
public class LabelReaderDataset : MonoBehaviour
{
    [Header("연결")]
    [Tooltip("송장 배치 에셋. BoxFeeder 가 쓰는 것과 같은 에셋이어야 한다.")]
    [SerializeField] LabelLayout layout;

    [Tooltip("공정용 상자 Prefab. BoxInfo 와 TMP 글자가 붙어 있어야 한다.")]
    [SerializeField] GameObject boxPrefab;

    [Tooltip("지역 코드 표를 가진 설정 에셋. BoxFeeder 가 읽는 것과 같은 에셋이어야 한다.")]
    [SerializeField] CellSettings settings;

    [Header("만들 양")]
    [Tooltip("상자 수. 상자 하나가 번호 한 장 + 지역명 한 장 = 이미지 2 장이다.")]
    [SerializeField, Min(1)] int sampleCount = 20;

    [Tooltip("난수 시드. 같은 값이면 같은 데이터셋이 나온다.")]
    [SerializeField] int randomSeed = 20261102;

    [Tooltip("프로젝트 폴더 아래 저장될 폴더 이름.")]
    [SerializeField] string datasetFolder = "LabelReaderDataset";

    [Header("흔들기")]
    [Tooltip("카메라를 LabelLayout 의 jitter 범위만큼 흔든다. 학습용이면 켠다.")]
    [SerializeField] bool jitter = true;

    [Tooltip("규칙 위반 송장 비율. 평가용 표본을 섞을 때 올린다 (FR-07).")]
    [SerializeField, Range(0f, 1f)] float wrongLabelRate = 0f;

    // 상자 Prefab 을 여러 개 넣어도 소용이 없다. 판독 카메라는 송장의 칸 하나만 찍고
    // 송장은 세 Prefab 이 똑같다 (3.6 x 4.5 cm). 다양성은 조명과 흔들림에서 나온다
    // (설계서 LabelTrainer: 조명 무작위화 세기 ±30%).
    [Tooltip("표본마다 세기를 흔들 조명. 공정 씬 판독 스테이션의 조명을 넣는다. 비우면 안 흔든다.")]
    [SerializeField] Light[] lights = new Light[0];

    [Tooltip("조명 세기를 흔드는 폭. 0.3 이면 ±30 % 다.")]
    [SerializeField, Range(0f, .5f)] float lightJitter = .3f;

    [Header("찍기")]
    [Tooltip("상자를 이 레이어에 올리고 카메라가 그 레이어만 보게 한다. 배경이 안 섞인다.")]
    [SerializeField] string boxLayerName = SampleBox.DefaultLayerName;

    [Tooltip("빈 곳의 색. 칸이 종이를 벗어났는지 한눈에 보이게 회색으로 둔다.")]
    [SerializeField] Color background = new Color(.5f, .5f, .5f);

    public string DatasetPath => Path.Combine(
        Path.GetDirectoryName(Application.dataPath) ?? ".", datasetFolder);

    /// <summary>이미지를 만들고 장수를 돌려준다.</summary>
    [ContextMenu("판독 데이터셋 생성")]
    public void Generate()
    {
        if (layout == null) { Debug.LogError("[판독셋] LabelLayout 을 연결해야 한다.", this); return; }
        if (layout.font == null) { Debug.LogError("[판독셋] LabelLayout 의 font 가 비어 있다.", layout); return; }
        if (boxPrefab == null) { Debug.LogError("[판독셋] 공정용 상자 Prefab 을 연결해야 한다.", this); return; }
        if (settings == null || settings.regionCodes == null || settings.regionCodes.Length == 0)
        { Debug.LogError("[판독셋] CellSettings 를 연결해야 한다. 지역 코드 표가 거기 있다.", this); return; }
        if (settings.regionNames == null || settings.regionNames.Length != settings.regionCodes.Length)
        { Debug.LogError("[판독셋] CellSettings 의 지역 코드와 이름 개수가 다르다.", settings); return; }

        string root = DatasetPath;
        string imageDir = Path.Combine(root, "images");
        Directory.CreateDirectory(imageDir);

        // 지난 생성물이 남아 있으면 labels.csv 가 가리키지 않는 고아 파일이 섞인다.
        int removed = 0;
        foreach (string old in Directory.GetFiles(imageDir, "*.png")) { File.Delete(old); removed++; }
        if (removed > 0) Debug.Log($"[판독셋] 이전 이미지 {removed} 장을 지우고 새로 만든다.", this);

        var rng = new System.Random(randomSeed);
        var csv = new StringBuilder();
        csv.AppendLine("file,sample,width,kind,answer,number,region,wrong");

        GameObject box = null;
        Camera cam = null;
        float[] baseIntensity = null;
        int written = 0;

        try
        {
            int layer = SampleBox.ResolveLayer(boxLayerName);
            if (layer < 0)
            {
                // 1 << -1 은 1 << 31 이 되어 아무것도 안 보이는 그림만 쌓인다. 조용히 넘기지 않는다.
                Debug.LogError($"[판독셋] '{boxLayerName}' 레이어가 없다. "
                             + "Edit → Project Settings → Tags and Layers 에서 만들거나, "
                             + "Unity 를 다시 켜서 설정을 다시 읽게 한다.", this);
                return;
            }

            box = Instantiate(boxPrefab);
            box.name = "ReaderBox (temp)";
            box.hideFlags = HideFlags.DontSave;
            SampleBox.SetLayerRecursively(box, layer);

            var info = box.GetComponent<BoxInfo>();
            if (info == null || info.label == null || info.numberText == null || info.regionText == null)
            {
                Debug.LogError("[판독셋] Prefab 에 BoxInfo · 송장 · TMP 글자가 연결돼 있어야 한다. "
                             + "Tools/Bonguri/Make Process Box Variants 로 만든 Prefab 을 쓴다.", this);
                return;
            }

            var camGo = new GameObject("ReaderCam (temp)") { hideFlags = HideFlags.DontSave };
            cam = camGo.AddComponent<Camera>();
            cam.enabled = false;                     // 매 프레임 그리지 않고 직접 Render 한다
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.cullingMask = 1 << layer;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            int px = Mathf.Max(8, layout.readPixels);

            // 글자가 칸을 제대로 채우는지 몇 장 찍어 보고 크기를 맞춘다. TMP 가 실제로
            // 그리는 높이는 글꼴 지표만으로 정확히 안 나와서, 그림에서 재는 편이 확실하다.
            Calibrate(layout, info, cam, px, settings.regionNames[0]);

            // 조명 세기는 끝나고 돌려놓는다. 씬의 조명을 빌려 쓰는 것이기 때문이다.
            baseIntensity = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) baseIntensity[i] = lights[i].intensity;

            for (int s = 0; s < sampleCount; s++)
            {
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] == null) continue;
                    float k = 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * lightJitter;
                    lights[i].intensity = baseIntensity[i] * k;
                }

                bool wrong = rng.NextDouble() < wrongLabelRate;
                MakeLabel(rng, wrong, out string number, out string region, out string kindOfWrong);
                layout.Print(info, number, region);

                // 번호는 5 자리를 한 장에 담는다. 자리마다 조준하면 칸이 조금만 어긋나도
                // 라벨과 그림이 안 맞는 표본이 조용히 섞인다. 가로로 길게 찍으므로
                // 자리당 해상도는 칸 하나만 찍을 때와 같다.
                for (int kind = 0; kind < 2; kind++)
                {
                    bool isRegion = kind == 1;
                    int w;

                    if (isRegion)
                    {
                        layout.AimAtRegion(cam, info.label, jitter);
                        w = px * 3;                               // 설계안 4.2 의 96 x 32
                    }
                    else
                    {
                        layout.AimAtNumber(cam, info.label, jitter);
                        w = Mathf.RoundToInt(px * layout.NumberBlockAspect);
                    }

                    var shot = Shoot(cam, w, px);

                    string name = string.Format(CultureInfo.InvariantCulture,
                        "{0:D4}_{1}.png", s, isRegion ? "region" : "number");
                    File.WriteAllBytes(Path.Combine(imageDir, name), shot.EncodeToPNG());
                    DestroyImmediate(shot);

                    string answer = isRegion ? region : number;
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "images/{0},{1},{2},{3},{4},{5},{6},{7}",
                        name, s, w, isRegion ? "region" : "number", answer, number, region, kindOfWrong));
                    written++;
                }
            }

            File.WriteAllText(Path.Combine(root, "labels.csv"), csv.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(root, "dataset_info.txt"), BuildInfo(written), Encoding.UTF8);
        }
        finally
        {
            if (baseIntensity != null)
                for (int i = 0; i < lights.Length && i < baseIntensity.Length; i++)
                    if (lights[i] != null) lights[i].intensity = baseIntensity[i];

            if (cam != null) DestroyImmediate(cam.gameObject);
            if (box != null) DestroyImmediate(box);
        }

        Debug.Log($"[판독셋] 이미지 {written} 장 생성 → {root}", this);
    }

    /// <summary>
    /// 숫자가 칸 높이의 fillRatio 만큼 차지하도록 LabelLayout.sizeTweak 을 맞춘다.
    /// 잉크 높이는 글자 크기에 비례하므로 두세 번이면 수렴한다.
    /// </summary>
    static void Calibrate(LabelLayout layout, BoxInfo info, Camera cam, int px, string probeRegion)
    {
        float want = px * layout.fillRatio / (1f + layout.camMargin);
        layout.sizeTweak = 1f;                       // 지난 번 값이 남아 꼬이지 않게 한다

        var log = new StringBuilder();
        log.AppendLine($"[판독셋] 글자 크기 맞추기 (목표 잉크 {want:F1} px / 그림 {px} px)");

        for (int pass = 0; pass < 14; pass++)
        {
            layout.Print(info, "88888", probeRegion);
            layout.AimAtNumber(cam, info.label, false);

            int w = Mathf.RoundToInt(px * layout.NumberBlockAspect);
            var shot = Shoot(cam, w, px);
            float ink = InkHeight(shot);
            DestroyImmediate(shot);

            if (ink < 1f)
            {
                log.AppendLine($"   {pass + 1,2}회  보정 {layout.sizeTweak:F4}  →  글자가 안 보인다");
                Debug.LogWarning(log.ToString() + "   크기를 그대로 두고 진행한다.");
                return;
            }

            // 글자가 그림보다 커서 잘리면 잉크 높이가 늘 그림 높이로 나온다. 얼마나 큰지
            // 알 수 없으므로 비례로 줄이지 않고 절반씩 줄여 잘리지 않는 데까지 내려온다.
            bool clipped = ink >= px - .5f;
            log.AppendLine($"   {pass + 1,2}회  보정 {layout.sizeTweak:F4}  잉크 {ink:F0} px"
                         + (clipped ? "  (잘림)" : ""));

            if (!clipped && Mathf.Abs(ink - want) <= 1f)
            {
                Debug.Log(log + $"   맞음. {pass + 1} 번 만에 보정 {layout.sizeTweak:F4}.");
                return;
            }

            layout.sizeTweak *= clipped ? .5f : want / ink;
        }

        Debug.LogWarning(log + $"   목표({want:F1} px)에 수렴하지 않았다. "
                       + $"보정 {layout.sizeTweak:F4} 으로 진행한다.");
    }

    /// <summary>그림에서 어두운 픽셀이 차지한 세로 높이 (px). 글자가 잘렸으면 전체 높이가 된다.</summary>
    static float InkHeight(Texture2D tex)
    {
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height;

        int lo = 255, hi = 0;
        foreach (var p in px) { if (p.r < lo) lo = p.r; if (p.r > hi) hi = p.r; }
        if (hi - lo < 20) return 0f;                 // 단색이면 글자가 없다

        int thr = (lo + hi) / 2, top = -1, bottom = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (px[y * w + x].r >= thr) continue;
                if (top < 0) top = y;
                bottom = y;
                break;
            }
        }
        return top < 0 ? 0f : bottom - top + 1;
    }

    /// <summary>카메라가 보는 그림을 회색조로 떠 온다.</summary>
    static Texture2D Shoot(Camera cam, int width, int height)
    {
        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture prevTarget = cam.targetTexture;

        // 세로 시야는 AimAt 이 칸 높이에서 잡았다. 가로는 그림 비율이 정한다.
        cam.aspect = (float)width / height;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevTarget;

        RenderTexture.active = rt;
        var colour = new Texture2D(width, height, TextureFormat.RGB24, false);
        colour.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        colour.Apply();

        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);

        // 설계안의 관측은 회색조다. 저장할 때 바꿔 두면 학습 쪽에서 다시 안 바꿔도 된다.
        var px = colour.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            byte g = (byte)((px[i].r * 299 + px[i].g * 587 + px[i].b * 114) / 1000);
            px[i] = new Color32(g, g, g, 255);
        }
        colour.SetPixels32(px);
        colour.Apply();
        return colour;
    }

    /// <summary>
    /// BoxFeeder.MakeLabel 과 같은 규칙이다. 지역 코드는 고르게, 물품번호 3 자리는 무작위.
    /// 공정에서는 일련번호의 끝 3 자리를 쓰지만 학습에는 고른 분포가 낫다.
    /// </summary>
    void MakeLabel(System.Random rng, bool wrong, out string number, out string region, out string kind)
    {
        int[] codes = settings.regionCodes;
        string[] names = settings.regionNames;

        int index = rng.Next(codes.Length);
        int code = codes[index];
        kind = "";

        if (wrong && codes.Length > 1)
        {
            if (rng.Next(2) == 0)
            {
                // 표에 없는 지역 코드 (FR-07 RegionError)
                do { code = rng.Next(10, 100); } while (System.Array.IndexOf(codes, code) >= 0);
                kind = "region";
            }
            else
            {
                // 번호와 다른 주소 지역 (FR-07 AddressMismatch)
                index = (index + rng.Next(1, codes.Length)) % codes.Length;
                kind = "address";
            }
        }

        var sb = new StringBuilder(LabelLayout.DigitCount);
        sb.Append(code.ToString("D2"));
        for (int i = 2; i < LabelLayout.DigitCount; i++) sb.Append((char)('0' + rng.Next(10)));

        number = sb.ToString();
        region = names[index];
    }

    string BuildInfo(int images)
    {
        var sb = new StringBuilder();
        sb.AppendLine("물류 분류 자동화 - FR-04 송장 판독 칸 이미지");
        sb.AppendLine($"생성 시각 : {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"상자 수   : {sampleCount} (이미지 {images} 장, 상자 1 개당 번호 1 + 지역명 1)");
        sb.AppendLine($"난수 시드 : {randomSeed}");
        sb.AppendLine($"해상도    : 번호 {Mathf.RoundToInt(layout.readPixels * layout.NumberBlockAspect)}"
                    + $" x {layout.readPixels} (5 자리 한 장), "
                    + $"지역명 {layout.readPixels * 3} x {layout.readPixels} (회색조)");
        sb.AppendLine($"카메라 흔들기 : {(jitter ? $"위치 ±{layout.jitterPos * 1000:F1} mm, 각도 ±{layout.jitterAngle:F0}°" : "없음")}");
        sb.AppendLine($"규칙 위반 비율 : {wrongLabelRate:P0}");
        sb.AppendLine($"조명 흔들기   : {(lights.Length > 0 ? $"{lights.Length} 개, 세기 ±{lightJitter:P0}" : "없음")}");
        sb.AppendLine();
        sb.AppendLine("■ 출전 (하나라도 바뀌면 다시 만든다)");
        sb.AppendLine($"  배치 에셋 : {layout.name}");
        sb.AppendLine($"  글꼴      : {layout.font.name} (bold {layout.bold})");
        sb.AppendLine($"  송장 크기 : {layout.labelSize.x * 100:F2} x {layout.labelSize.y * 100:F2} cm");
        sb.AppendLine($"  칸 여백   : {layout.camMargin:P0}");
        sb.AppendLine($"  상자      : {boxPrefab.name}");
        sb.AppendLine();
        sb.AppendLine("labels.csv 열");
        sb.AppendLine("  file   : images/ 아래 파일 경로");
        sb.AppendLine("  sample : 상자 번호 (같은 번호 2 줄 = 한 상자)");
        sb.AppendLine("  width  : 그 그림의 가로 픽셀 수 (세로는 항상 " + layout.readPixels + ")");
        sb.AppendLine("  kind   : number (번호 5 자리 한 장) 또는 region (지역명)");
        sb.AppendLine("  answer : 그 그림의 정답. number 면 5 자리 문자열, region 이면 지역 이름");
        sb.AppendLine("  number : 그 상자의 송장 번호 5 자리");
        sb.AppendLine("  region : 그 상자의 주소 지역");
        sb.AppendLine("  wrong  : 규칙 위반 종류 (region = 없는 코드, address = 주소 불일치)");
        return sb.ToString();
    }
}
