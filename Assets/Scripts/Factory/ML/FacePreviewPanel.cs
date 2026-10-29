using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 판정 카메라 5대가 찍는 그림을 게임 화면 구석에 띄운다 (UI-01 "상단 카메라 미리보기").
/// 카메라 Culling Mask 가 상자 레이어만 보게 돼 있으므로,
/// 여기 보이는 그림이 곧 CNN 에 들어가는 입력과 같다.
/// </summary>
[DisallowMultipleComponent]
public class FacePreviewPanel : MonoBehaviour
{
    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    const string CanvasName = "FacePreviewCanvas";
    const int LabelHeight = 16;

    [SerializeField] FaceCameraRig rig;

    [Header("자리")]
    [SerializeField] Corner corner = Corner.TopLeft;
    [SerializeField, Min(48)] int tileSize = 120;
    [SerializeField, Min(0)] int padding = 8;

    [Header("갱신")]
    [Tooltip("초 단위. 0 이면 매 프레임 갱신한다.")]
    [SerializeField, Min(0f)] float refreshInterval = 0.1f;

    [Tooltip("면마다 송장 색 비율을 함께 보여준다. GPU 에서 픽셀을 읽어오므로 끄면 더 가볍다.")]
    [SerializeField] bool showRatio;

    [System.NonSerialized] RawImage[] images;
    [System.NonSerialized] Text[] labels;
    [System.NonSerialized] Texture2D readback;
    float timer;

    public FaceCameraRig Rig => rig;
    public bool IsBuilt => transform.Find(CanvasName) != null;

    void Reset()
    {
        rig = GetComponentInParent<FaceCameraRig>();
        if (rig == null) rig = GetComponentInChildren<FaceCameraRig>();
    }

    void OnEnable()
    {
        if (rig == null) rig = GetComponentInParent<FaceCameraRig>();
        if (IsBuilt) Bind();
    }

    void OnDisable()
    {
        if (readback != null)
        {
            if (Application.isPlaying) Destroy(readback); else DestroyImmediate(readback);
            readback = null;
        }
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        timer += Time.unscaledDeltaTime;
        if (refreshInterval > 0f && timer < refreshInterval) return;
        timer = 0f;
        Refresh();
    }

    // ── 패널 만들기 ──────────────────────────────────────────────

    /// <summary>캔버스와 타일 5개를 새로 만든다. 이미 있으면 지우고 다시 만든다.</summary>
    public void Build()
    {
        if (rig == null)
        {
            Debug.LogError("[FacePreviewPanel] FaceCameraRig 를 연결해야 한다.", this);
            return;
        }

        var old = transform.Find(CanvasName);
        if (old != null) Kill(old.gameObject);

        var canvasGo = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        Font font = LoadFont();
        images = new RawImage[FaceCameraRig.FaceCount];
        labels = new Text[FaceCameraRig.FaceCount];

        for (int i = 0; i < FaceCameraRig.FaceCount; i++)
        {
            BoxFace face = FaceCameraRig.CapturedFaces[i];

            var tile = new GameObject($"Tile_{face}", typeof(RectTransform));
            tile.transform.SetParent(canvasGo.transform, false);
            var rect = (RectTransform)tile.transform;
            ApplyCorner(rect, i);

            var imageGo = new GameObject("Image", typeof(RawImage));
            imageGo.transform.SetParent(tile.transform, false);
            var imageRect = (RectTransform)imageGo.transform;
            imageRect.anchorMin = new Vector2(0f, 0f);
            imageRect.anchorMax = new Vector2(1f, 1f);
            imageRect.offsetMin = new Vector2(0f, LabelHeight);
            imageRect.offsetMax = Vector2.zero;
            images[i] = imageGo.GetComponent<RawImage>();

            if (font == null) continue;

            var labelGo = new GameObject("Label", typeof(Text));
            labelGo.transform.SetParent(tile.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = new Vector2(0f, LabelHeight);

            var text = labelGo.GetComponent<Text>();
            text.font = font;
            text.fontSize = 11;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = face.ToString();
            labels[i] = text;
        }

        Refresh();
    }

    public void Clear()
    {
        var old = transform.Find(CanvasName);
        if (old != null) Kill(old.gameObject);
        images = null;
        labels = null;
        if (rig != null) rig.ReleaseLiveTextures();
    }

    /// <summary>이미 만들어진 타일을 다시 찾아 연결한다 (도메인 리로드 뒤).</summary>
    void Bind()
    {
        var canvasTr = transform.Find(CanvasName);
        if (canvasTr == null) return;

        images = new RawImage[FaceCameraRig.FaceCount];
        labels = new Text[FaceCameraRig.FaceCount];

        for (int i = 0; i < FaceCameraRig.FaceCount; i++)
        {
            var tile = canvasTr.Find($"Tile_{FaceCameraRig.CapturedFaces[i]}");
            if (tile == null) continue;

            var image = tile.Find("Image");
            if (image != null) images[i] = image.GetComponent<RawImage>();

            var label = tile.Find("Label");
            if (label != null) labels[i] = label.GetComponent<Text>();
        }
    }

    // ── 갱신 ────────────────────────────────────────────────────

    public void Refresh()
    {
        if (rig == null || !rig.IsBuilt) return;

        RenderTexture[] live = rig.EnsureLiveTextures();
        if (live == null) return;
        rig.RenderLive();

        if (images == null) Bind();
        if (images == null) return;

        for (int i = 0; i < FaceCameraRig.FaceCount; i++)
        {
            if (images[i] != null) images[i].texture = live[i];
            if (labels == null || labels[i] == null) continue;

            BoxFace face = FaceCameraRig.CapturedFaces[i];
            labels[i].text = showRatio
                ? $"{face}  {ReadRatio(live[i]):P1}"
                : face.ToString();
        }
    }

    float ReadRatio(RenderTexture rt)
    {
        if (rt == null) return 0f;

        if (readback == null || readback.width != rt.width || readback.height != rt.height)
        {
            if (readback != null) Kill(readback);
            readback = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false)
            {
                hideFlags = HideFlags.DontSave,
            };
        }

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        readback.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);
        readback.Apply();
        RenderTexture.active = prev;

        return rig.LabelRatio(readback);
    }

    // ── 자리 잡기 ────────────────────────────────────────────────

    void ApplyCorner(RectTransform rect, int index)
    {
        rect.sizeDelta = new Vector2(tileSize, tileSize + LabelHeight);

        float step = tileSize + padding;
        float x = padding + index * step;
        float y = padding;

        switch (corner)
        {
            case Corner.TopLeft:
                SetAnchor(rect, new Vector2(0f, 1f));
                rect.anchoredPosition = new Vector2(x, -y);
                break;
            case Corner.TopRight:
                SetAnchor(rect, new Vector2(1f, 1f));
                rect.anchoredPosition = new Vector2(-x, -y);
                break;
            case Corner.BottomLeft:
                SetAnchor(rect, new Vector2(0f, 0f));
                rect.anchoredPosition = new Vector2(x, y);
                break;
            default:
                SetAnchor(rect, new Vector2(1f, 0f));
                rect.anchoredPosition = new Vector2(-x, y);
                break;
        }
    }

    static void SetAnchor(RectTransform rect, Vector2 anchor)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
    }

    static Font LoadFont()
    {
        Font font = null;
        try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { /* 버전에 따라 없을 수 있다 */ }

        if (font == null)
        {
            try { font = Font.CreateDynamicFontFromOSFont("Arial", 12); }
            catch { /* 글꼴을 못 구하면 이름표 없이 그림만 띄운다 */ }
        }
        return font;
    }

    static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
