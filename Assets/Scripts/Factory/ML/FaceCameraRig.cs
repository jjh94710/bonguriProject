using UnityEngine;

/// <summary>
/// FR-03 ① 판정 카메라 리그.
/// 스캔 대기점에 멈춘 상자의 위면과 측면 4 개(총 5 면)를 찍는다.
/// 찍은 이미지는 CNN 학습 이미지(LabelDatasetGenerator)와 송장 색 비율 측정에 함께 쓴다.
///
/// 배치 기준은 SRS 2.5(상자 0.4 x 0.3 x 0.25 m)이고, 카메라는 월드 축에 고정이다.
/// 상자가 90 도 / 180 도 돌아가도 같은 카메라가 그 자리에 온 면을 찍는다.
/// </summary>
[DisallowMultipleComponent]
public class FaceCameraRig : MonoBehaviour
{
    public const int FaceCount = 5;

    /// <summary>카메라 배열과 같은 순서. Bottom 은 카메라가 없다.</summary>
    public static readonly BoxFace[] CapturedFaces =
    {
        BoxFace.Top, BoxFace.Right, BoxFace.Back, BoxFace.Left, BoxFace.Front,
    };

    [Header("배치 기준")]
    [Tooltip("상자가 멈추는 지점. 비우면 이 오브젝트 자신을 쓴다.")]
    [SerializeField] Transform stopPoint;

    [Tooltip("기준점이 상자 중심이면 체크. 비워두면 벨트 윗면으로 보고 상자 높이의 절반만큼 올린다.")]
    [SerializeField] bool stopPointIsBoxCenter;

    [Tooltip("상자 크기 (m). SRS 2.5 기준 가로 0.4 / 높이 0.25 / 세로 0.3")]
    [SerializeField] Vector3 boxSize = new Vector3(0.4f, 0.25f, 0.3f);

    [Tooltip("시야 여유 비율. 상자가 돌아가도 잘리지 않게 한다.")]
    [SerializeField, Range(0f, 1f)] float margin = 0.15f;

    [Tooltip("상자 중심에서 카메라까지 거리 (m).")]
    [SerializeField, Min(0.05f)] float cameraDistance = 0.6f;

    [Header("촬영")]
    [Tooltip("한 변 픽셀 수. FR-03 입력 기준은 84 픽셀 이상이다.")]
    [SerializeField, Min(84)] int resolution = 96;

    [SerializeField] LayerMask cullingMask = ~0;
    [SerializeField] Color background = Color.black;

    [Header("송장 색 비율 (기본 제어 판정)")]
    // 송장 바탕색을 절대 RGB 로 맞춰보던 때는 빛을 덜 받는 앞면·왼쪽면 송장을 놓쳤다.
    // 평가셋 100 상자 중 91 개만 맞아 FR-03 인수 기준(95 개)에 못 미쳤다.
    // 채도는 밝기에 같이 곱해지지 않으므로, 어두운 면에서도 흰 종이를 그대로 가려낸다.
    [Tooltip("채도가 이 값보다 낮으면 송장 종이로 본다. 골판지 0.71, 테이프 0.57, 송장 0.10.")]
    [SerializeField, Range(0f, 1f)] float labelMaxSaturation = 0.35f;

    [Tooltip("이보다 어두우면 배경으로 보고 뺀다. 검은 배경도 채도가 0 이라 같이 걸리는 것을 막는다.")]
    [SerializeField, Range(0f, 1f)] float labelMinBrightness = 0.35f;

    [Tooltip("이 비율 이상이면 그 면에 송장이 보인다고 본다.")]
    [SerializeField, Range(0f, 1f)] float labelPixelRatio = 0.05f;

    [SerializeField, HideInInspector] Camera[] faceCameras = new Camera[FaceCount];

    // 게임 화면 미리보기용 상시 출력. 도메인 리로드 때 날아가면 다시 만든다.
    [System.NonSerialized] RenderTexture[] liveTextures;

    public Transform StopPoint => stopPoint != null ? stopPoint : transform;
    public Vector3 BoxSize => boxSize;
    public int Resolution => resolution;
    public Camera[] FaceCameras => faceCameras;
    public LayerMask CullingMask => cullingMask;

    /// <summary>Culling Mask 가 모든 레이어를 보고 있으면 배경과 로봇까지 찍힌다.</summary>
    public bool SeesEverything => cullingMask.value == ~0;

    /// <summary>상자 중심의 월드 좌표.</summary>
    public Vector3 BoxCenter
    {
        get
        {
            Vector3 p = StopPoint.position;
            return stopPointIsBoxCenter ? p : p + Vector3.up * (boxSize.y * 0.5f);
        }
    }

    public bool IsBuilt
    {
        get
        {
            if (faceCameras == null || faceCameras.Length != FaceCount) return false;
            for (int i = 0; i < FaceCount; i++) if (faceCameras[i] == null) return false;
            return true;
        }
    }

    // ── 리그 만들기 ──────────────────────────────────────────────

    /// <summary>카메라 5 대를 만들거나 현재 설정에 맞춰 다시 배치한다.</summary>
    public void BuildRig()
    {
        if (faceCameras == null || faceCameras.Length != FaceCount)
            faceCameras = new Camera[FaceCount];

        // 상자가 어떤 자세로 서 있어도 잘리지 않도록 가장 긴 변을 기준으로 시야를 잡는다.
        float maxDim = Mathf.Max(boxSize.x, Mathf.Max(boxSize.y, boxSize.z));
        float orthoSize = maxDim * 0.5f * (1f + margin);

        for (int i = 0; i < FaceCount; i++)
        {
            BoxFace face = CapturedFaces[i];
            Camera cam = faceCameras[i];
            if (cam == null)
            {
                var go = new GameObject($"FaceCam_{face}");
                go.transform.SetParent(transform, false);
                cam = go.AddComponent<Camera>();
                faceCameras[i] = cam;
            }

            Vector3 outward = OutwardOf(face);
            cam.transform.position = BoxCenter + outward * cameraDistance;
            cam.transform.rotation = Quaternion.LookRotation(-outward, UpOf(face));

            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
            cam.aspect = 1f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = cameraDistance + maxDim * 2f;
            cam.cullingMask = cullingMask;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            // 매 프레임 그리지 않고 Capture() 에서 직접 Render() 한다.
            cam.enabled = false;
        }
    }

    /// <summary>해당 면이 바라보는 월드 방향.</summary>
    public static Vector3 OutwardOf(BoxFace face)
    {
        switch (face)
        {
            case BoxFace.Top: return Vector3.up;
            case BoxFace.Bottom: return Vector3.down;
            case BoxFace.Right: return Vector3.right;
            case BoxFace.Left: return Vector3.left;
            case BoxFace.Back: return Vector3.forward;
            case BoxFace.Front: return Vector3.back;
            default: return Vector3.up;
        }
    }

    static Vector3 UpOf(BoxFace face)
    {
        // 위/아래를 볼 때는 월드 up 을 기준으로 쓸 수 없다.
        return (face == BoxFace.Top || face == BoxFace.Bottom) ? Vector3.forward : Vector3.up;
    }

    // ── 게임 화면 미리보기 (UI-01 상단 카메라 미리보기) ──────────────

    /// <summary>면마다 상시 RenderTexture 를 붙인다. 이미 있으면 그대로 쓴다.</summary>
    public RenderTexture[] EnsureLiveTextures()
    {
        if (!IsBuilt) return null;

        if (liveTextures == null || liveTextures.Length != FaceCount)
            liveTextures = new RenderTexture[FaceCount];

        for (int i = 0; i < FaceCount; i++)
        {
            RenderTexture rt = liveTextures[i];
            if (rt != null && (rt.width != resolution || rt.height != resolution))
            {
                rt.Release();
                SafeKill(rt);
                rt = null;
            }
            if (rt == null)
            {
                rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"Live_{CapturedFaces[i]}",
                    hideFlags = HideFlags.DontSave,
                };
                rt.Create();
                liveTextures[i] = rt;
            }
            faceCameras[i].targetTexture = rt;
        }
        return liveTextures;
    }

    /// <summary>미리보기 텍스처를 한 번 갱신한다.</summary>
    public void RenderLive()
    {
        if (liveTextures == null || !IsBuilt) return;
        for (int i = 0; i < FaceCount; i++)
        {
            if (faceCameras[i] != null && liveTextures[i] != null) faceCameras[i].Render();
        }
    }

    public void ReleaseLiveTextures()
    {
        if (liveTextures == null) return;
        for (int i = 0; i < FaceCount; i++)
        {
            if (faceCameras != null && i < faceCameras.Length && faceCameras[i] != null)
                faceCameras[i].targetTexture = null;

            if (liveTextures[i] == null) continue;
            liveTextures[i].Release();
            SafeKill(liveTextures[i]);
        }
        liveTextures = null;
    }

    void OnDisable() => ReleaseLiveTextures();

    static void SafeKill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    // ── 촬영 ────────────────────────────────────────────────────

    /// <summary>
    /// 5 면을 찍어 Texture2D 5 장을 돌려준다. 다 쓴 뒤에는 ReleaseShots 로 지운다.
    /// </summary>
    public Texture2D[] Capture()
    {
        var shots = new Texture2D[FaceCount];
        if (!IsBuilt)
        {
            Debug.LogWarning("[FaceCameraRig] 카메라가 아직 없다. BuildRig() 먼저.", this);
            return shots;
        }

        var rt = RenderTexture.GetTemporary(resolution, resolution, 24, RenderTextureFormat.ARGB32);
        RenderTexture prevActive = RenderTexture.active;

        for (int i = 0; i < FaceCount; i++)
        {
            Camera cam = faceCameras[i];
            RenderTexture prevTarget = cam.targetTexture;

            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prevTarget;

            RenderTexture.active = rt;
            var tex = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            tex.Apply();
            shots[i] = tex;
        }

        RenderTexture.active = prevActive;
        RenderTexture.ReleaseTemporary(rt);
        return shots;
    }

    public static void ReleaseShots(Texture2D[] shots)
    {
        if (shots == null) return;
        for (int i = 0; i < shots.Length; i++)
        {
            if (shots[i] == null) continue;
            if (Application.isPlaying) Destroy(shots[i]);
            else DestroyImmediate(shots[i]);
        }
    }

    // ── 색 비율 측정 (학습 모델이 없을 때의 기본 판정) ──────────────

    /// <summary>5 면의 송장 색 비율. 배열 순서는 CapturedFaces 와 같다.</summary>
    public float[] Measure()
    {
        var ratios = new float[FaceCount];
        Texture2D[] shots = Capture();
        for (int i = 0; i < FaceCount; i++)
        {
            if (shots[i] != null) ratios[i] = LabelRatio(shots[i]);
        }
        ReleaseShots(shots);
        return ratios;
    }

    /// <summary>
    /// 그림에서 송장 종이로 보이는 픽셀 비율. 채도가 낮고 충분히 밝은 픽셀을 센다.
    /// 5000 장으로 재보니 송장이 보이는 면은 최소 0.080, 안 보이는 면은 최대 0.004 였다.
    /// 기준값 labelPixelRatio(0.05) 가 그 사이에 있어 여유가 22 배다.
    /// </summary>
    public float LabelRatio(Texture2D tex)
    {
        Color32[] px = tex.GetPixels32();
        if (px.Length == 0) return 0f;

        float vmin = labelMinBrightness * 255f;

        int hit = 0;
        for (int i = 0; i < px.Length; i++)
        {
            int r = px[i].r, g = px[i].g, b = px[i].b;

            int max = r > g ? (r > b ? r : b) : (g > b ? g : b);
            if (max <= vmin) continue;                               // 배경

            int min = r < g ? (r < b ? r : b) : (g < b ? g : b);
            // 채도 = (max - min) / max. 나눗셈을 피해 양변에 max 를 곱해 비교한다.
            if (max - min < labelMaxSaturation * max) hit++;
        }
        return (float)hit / px.Length;
    }

    /// <summary>
    /// 색 비율이 가장 높은 면. 어느 면도 기준을 넘지 못하면 Bottom 으로 본다 (SRS FR-03 처리 3).
    /// 카메라가 5 면만 보므로, 다섯 면에서 송장이 안 보이면 남은 곳은 바닥뿐이다.
    /// </summary>
    public BoxFace JudgeFace(float[] ratios)
    {
        if (ratios == null || ratios.Length < FaceCount)
        {
            // 측정 자체가 실패한 경우다. 부르는 쪽에서 ExceptionReason.JudgeFail 로 따로 기록한다.
            Debug.LogWarning("[FaceCameraRig] 비율 배열이 비었다. 바닥으로 판정한다.", this);
            return BoxFace.Bottom;
        }

        int best = -1;
        float bestValue = 0f;
        for (int i = 0; i < FaceCount; i++)
        {
            if (ratios[i] > bestValue) { bestValue = ratios[i]; best = i; }
        }
        return (best >= 0 && bestValue >= labelPixelRatio) ? CapturedFaces[best] : BoxFace.Bottom;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireCube(BoxCenter, boxSize);

        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        for (int i = 0; i < FaceCount; i++)
        {
            Vector3 outward = OutwardOf(CapturedFaces[i]);
            Gizmos.DrawLine(BoxCenter, BoxCenter + outward * cameraDistance);
        }
    }
}
