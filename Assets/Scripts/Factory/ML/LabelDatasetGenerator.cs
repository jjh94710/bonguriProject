using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// FR-03 ① 학습 이미지 자동 생성.
/// 송장면 방향을 FR-01 비율(위 70 / 측면 각 5 / 바닥 10 %)로 무작위 배정한 상자를
/// 스캔 대기점에 세우고 FaceCameraRig 로 5 면을 찍어 PNG 와 labels.csv 를 만든다.
///
/// 결과는 Assets 밖(프로젝트 루트/LabelDataset)에 쓴다.
/// Assets 안에 수천 장을 넣으면 유니티가 전부 임포트해서 에디터가 크게 느려진다.
/// </summary>
[DisallowMultipleComponent]
public class LabelDatasetGenerator : MonoBehaviour
{
    /// <summary>학습에 쓸 상자 한 종류. Prefab 마다 송장이 붙은 면이 다르므로
    /// 방향을 각자 들고 있어야 한다. 칸이 하나뿐이면 섞어 쓸 때 전부 틀어진다.</summary>
    [System.Serializable]
    public class BoxEntry
    {
        [Tooltip("Project 창에서 끌어다 넣는다. Hierarchy 의 씬 오브젝트를 넣으면 그 오브젝트를 지울 때 끊긴다.")]
        public GameObject prefab;

        [Tooltip("이 Prefab 기준으로 송장이 붙은 면의 방향. vertical (0,1,0) / horizontal (1,0,0) / up Variant (0,0,-1)")]
        public Vector3 labelLocalNormal = Vector3.up;

        [Tooltip("뽑힐 가중치. 전부 1 이면 균등하게 섞인다.")]
        [Min(0f)] public float weight = 1f;

        public Vector3 Normal =>
            labelLocalNormal.sqrMagnitude < 1e-6f ? Vector3.up : labelLocalNormal.normalized;
        public string Name => prefab != null ? prefab.name : "임시 상자";
    }

    [Header("연결")]
    [SerializeField] FaceCameraRig rig;

    [Tooltip("학습에 쓸 상자들. 여러 개 넣으면 가중치대로 섞어 뽑는다. Prefab 마다 송장이 붙은 면의 넓이가 다르므로 섞는 편이 좋다. 비우면 임시 상자를 쓴다.")]
    [SerializeField] BoxEntry[] boxes = new BoxEntry[0];

    // 예전 단일 Box Prefab 설정. OnValidate 가 목록 첫 칸으로 한 번 옮기고 비운다.
    [SerializeField, HideInInspector] GameObject boxPrefab;
    [SerializeField, HideInInspector] Vector3 labelLocalNormal = Vector3.forward;

    [Tooltip("송장 오브젝트 이름. 송장 미부착 샘플을 만들 때 이 자식을 끈다. 조원 Prefab 이름에 맞춘다.")]
    [SerializeField] string labelChildName = "Label";

    [Tooltip("상자를 올려둘 레이어. 리그 Culling Mask 를 이 레이어만으로 두면 배경과 로봇이 안 찍힌다.")]
    [SerializeField] string boxLayerName = SampleBox.DefaultLayerName;

    [Header("생성량")]
    [SerializeField, Min(1)] int sampleCount = 300;
    [SerializeField] int randomSeed = 20261029;
    [SerializeField] string datasetFolder = "LabelDataset";

    [Header("송장면 방향 비율 (FR-01)")]
    [SerializeField, Range(0f, 1f)] float topRatio = 0.70f;
    [SerializeField, Range(0f, 1f)] float bottomRatio = 0.10f;
    // 나머지는 측면 4 방향에 균등 배분한다 (기본값이면 각 5 %).

    [Tooltip("송장 미부착 상자를 섞는다. CNN 이 '5 면 모두 없음'을 배우는 데 쓴다 (FR-06).")]
    [SerializeField] bool includeNoLabelSamples = true;
    [SerializeField, Range(0f, 0.5f)] float noLabelRatio = 0.05f;

    [Header("변화 주기")]
    [Tooltip("면 안에서 상자를 90 도 단위로 돌린다.")]
    [SerializeField] bool quarterTurnJitter = true;
    [SerializeField, Range(0f, 10f)] float angleJitter = 2.5f;
    [SerializeField, Range(0f, 0.1f)] float positionJitter = 0.02f;

    // 에디터 프리뷰는 목록 첫 칸으로 찍어본다.
    public GameObject BoxPrefab => boxes != null && boxes.Length > 0 ? boxes[0].prefab : null;
    public Vector3 LabelLocalNormal => boxes != null && boxes.Length > 0 ? boxes[0].Normal : Vector3.up;
    public string BoxLayerName => boxLayerName;

    void OnValidate()
    {
        // 예전 단일 Box Prefab 설정을 목록 첫 칸으로 한 번 옮긴다.
        if ((boxes == null || boxes.Length == 0) && boxPrefab != null)
        {
            boxes = new[] { new BoxEntry { prefab = boxPrefab, labelLocalNormal = labelLocalNormal } };
            boxPrefab = null;
        }
    }

    /// <summary>쓸 수 있는 상자 목록. 하나도 없으면 임시 상자 한 종류를 돌려준다.</summary>
    IEnumerable<BoxEntry> Entries()
    {
        bool any = false;
        if (boxes != null)
        {
            foreach (var e in boxes)
            {
                if (e == null || e.prefab == null || e.weight <= 0f) continue;
                any = true;
                yield return e;
            }
        }
        if (!any) yield return new BoxEntry { prefab = null, labelLocalNormal = Vector3.forward };
    }

    public string DatasetPath =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", datasetFolder));

    /// <summary>데이터셋을 만든다. 만들어진 이미지 장수를 돌려준다.</summary>
    public int Generate()
    {
        if (rig == null) rig = GetComponentInChildren<FaceCameraRig>();
        if (rig == null)
        {
            Debug.LogError("[LabelDataset] FaceCameraRig 를 연결해야 한다.", this);
            return 0;
        }
        if (!rig.IsBuilt)
        {
            Debug.LogError("[LabelDataset] 카메라 리그가 아직 없다. 먼저 '카메라 5대 배치'.", this);
            return 0;
        }

        string root = DatasetPath;
        string imageDir = Path.Combine(root, "images");
        Directory.CreateDirectory(imageDir);

        // 지난 생성물이 남아 있으면 labels.csv 가 가리키지 않는 고아 파일이 섞인다.
        // 폴더만 훑어 학습하면 정답 없는 이미지를 끌어다 쓰게 되므로 먼저 비운다.
        int removed = 0;
        foreach (string old in Directory.GetFiles(imageDir, "*.png"))
        {
            File.Delete(old);
            removed++;
        }
        if (removed > 0) Debug.Log($"[LabelDataset] 이전 이미지 {removed} 장을 지우고 새로 만든다.", this);

        var rng = new System.Random(randomSeed);
        var csv = new StringBuilder();
        csv.AppendLine("file,sample,face,has_label,true_face,box");

        var temps = new List<Object>();
        var spawned = new List<(BoxEntry entry, GameObject go, GameObject label)>();
        int written = 0;

        try
        {
            // 상자 종류마다 하나씩 미리 세워두고 샘플마다 하나만 켠다.
            // 매번 Instantiate 하면 수천 장에서 눈에 띄게 느려진다.
            foreach (var e in Entries())
            {
                var go = SpawnBox(e.prefab, temps);
                var lab = SampleBox.FindLabel(go, labelChildName);
                if (includeNoLabelSamples && lab == null)
                {
                    Debug.LogWarning(
                        $"[LabelDataset] '{e.Name}' 에서 송장('{labelChildName}')을 못 찾았다. " +
                        "이 상자는 미부착 샘플을 빼고 만든다.", this);
                }
                go.SetActive(false);
                spawned.Add((e, go, lab));
            }

            // 흔들림이 크면 상자가 프레임 밖으로 잘린 채 찍힌다. 이미지만 보면 멀쩡해
            // 보이므로 사람이 알아채기 어렵다. 카메라에서 역산한 값으로 묶어둔다.
            float jitterRange = positionJitter;
            float safeJitter = SafeJitter();
            if (jitterRange > safeJitter)
            {
                Debug.LogWarning(
                    $"[LabelDataset] Position Jitter {positionJitter:F3} m 는 화면을 벗어난다. " +
                    $"{safeJitter:F3} m 로 줄여서 만든다.", this);
                jitterRange = safeJitter;
            }

            // 송장을 못 찾은 상자는 끌 수가 없어 미부착 샘플에서 빠진다.
            bool makeNoLabel = includeNoLabelSamples && spawned.Exists(x => x.label != null);
            float totalWeight = 0f;
            foreach (var x in spawned) totalWeight += Mathf.Max(0f, x.entry.weight);

            for (int s = 0; s < sampleCount; s++)
            {
                var pick = PickBox(spawned, totalWeight, rng);

                bool noLabel = makeNoLabel && pick.label != null && rng.NextDouble() < noLabelRatio;
                if (pick.label != null) pick.label.SetActive(!noLabel);

                BoxFace target = noLabel ? BoxFace.None : PickFace(rng);
                pick.go.SetActive(true);
                PlaceBox(pick.go.transform, target, rng, jitterRange, pick.entry.Normal);

                // 실제 자세에서 송장이 어느 면에 있는지 다시 계산한다(배치가 틀려도 정답은 맞게).
                BoxFace trueFace = noLabel ? BoxFace.None : ActualFace(pick.go.transform, pick.entry.Normal);

                Texture2D[] shots = rig.Capture();
                for (int i = 0; i < FaceCameraRig.FaceCount; i++)
                {
                    if (shots[i] == null) continue;

                    BoxFace face = FaceCameraRig.CapturedFaces[i];
                    string name = string.Format(CultureInfo.InvariantCulture, "{0:D4}_{1}.png", s, face);
                    File.WriteAllBytes(Path.Combine(imageDir, name), shots[i].EncodeToPNG());

                    int hasLabel = (face == trueFace) ? 1 : 0;
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "images/{0},{1},{2},{3},{4},{5}", name, s, face, hasLabel, trueFace, pick.entry.Name));
                    written++;
                }
                FaceCameraRig.ReleaseShots(shots);
                pick.go.SetActive(false);
            }

            File.WriteAllText(Path.Combine(root, "labels.csv"), csv.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(root, "dataset_info.txt"), BuildInfo(written, makeNoLabel), Encoding.UTF8);
        }
        finally
        {
            foreach (var t in temps) SafeDestroy(t);
            foreach (var x in spawned) if (x.go != null) SafeDestroy(x.go);
        }

        Debug.Log($"[LabelDataset] 이미지 {written} 장 생성 → {root}");
        return written;
    }

    // ── 면 고르기 / 상자 세우기 ──────────────────────────────────

    /// <summary>가중치대로 상자 한 종류를 뽑는다.</summary>
    static (BoxEntry entry, GameObject go, GameObject label) PickBox(
        List<(BoxEntry entry, GameObject go, GameObject label)> spawned, float totalWeight, System.Random rng)
    {
        if (spawned.Count == 1 || totalWeight <= 0f) return spawned[0];

        double r = rng.NextDouble() * totalWeight;
        for (int i = 0; i < spawned.Count; i++)
        {
            r -= Mathf.Max(0f, spawned[i].entry.weight);
            if (r <= 0.0) return spawned[i];
        }
        return spawned[spawned.Count - 1];
    }

    BoxFace PickFace(System.Random rng)
    {
        float top = Mathf.Clamp01(topRatio);
        float bottom = Mathf.Clamp01(bottomRatio);
        float side = Mathf.Max(0f, 1f - top - bottom) * 0.25f;

        double r = rng.NextDouble();
        if (r < top) return BoxFace.Top;
        r -= top;
        if (r < bottom) return BoxFace.Bottom;
        r -= bottom;

        if (r < side) return BoxFace.Right;
        r -= side;
        if (r < side) return BoxFace.Back;
        r -= side;
        if (r < side) return BoxFace.Left;
        return BoxFace.Front;
    }

    /// <summary>상자가 화면 밖으로 나가지 않는 최대 흔들림(m).
    /// 카메라의 실제 직교 범위에서 역산하므로 Box Size 나 Margin 을 바꿔도 따라온다.</summary>
    float SafeJitter()
    {
        var cams = rig.FaceCameras;
        if (cams == null || cams.Length == 0 || cams[0] == null) return positionJitter;

        Vector3 b = rig.BoxSize;
        float maxDim = Mathf.Max(b.x, Mathf.Max(b.y, b.z));

        // 90 도 단위로 돌리므로 화면에서 차지하는 반폭은 가장 긴 변의 절반이고,
        // 거기에 angleJitter 만큼 비스듬해지는 몫을 더한다.
        float inPlaneHalf = maxDim * 0.5f * (1f + Mathf.Sin(angleJitter * Mathf.Deg2Rad));
        return Mathf.Max(0f, cams[0].orthographicSize - inPlaneHalf);
    }

    void PlaceBox(Transform box, BoxFace target, System.Random rng, float jitterRange, Vector3 labelNormal)
    {
        Vector3 wanted = (target == BoxFace.None)
            ? FaceCameraRig.OutwardOf(BoxFace.Top)
            : FaceCameraRig.OutwardOf(target);

        Quaternion baseRot = Quaternion.FromToRotation(labelNormal, wanted);

        float spin = 0f;
        if (quarterTurnJitter) spin = rng.Next(0, 4) * 90f;
        spin += (float)(rng.NextDouble() * 2.0 - 1.0) * angleJitter;

        box.rotation = Quaternion.AngleAxis(spin, wanted) * baseRot;

        Vector3 jitter = new Vector3(
            (float)(rng.NextDouble() * 2.0 - 1.0),
            0f,
            (float)(rng.NextDouble() * 2.0 - 1.0)) * jitterRange;
        box.position = rig.BoxCenter + jitter;
    }

    /// <summary>지금 자세에서 송장면이 향하는 월드 방향에 가장 가까운 면.</summary>
    BoxFace ActualFace(Transform box, Vector3 labelNormal)
    {
        Vector3 n = box.TransformDirection(labelNormal).normalized;

        BoxFace best = BoxFace.Top;
        float bestDot = float.NegativeInfinity;
        for (int f = 0; f <= (int)BoxFace.Bottom; f++)
        {
            var face = (BoxFace)f;
            float d = Vector3.Dot(n, FaceCameraRig.OutwardOf(face));
            if (d > bestDot) { bestDot = d; best = face; }
        }
        return best;
    }

    // ── 상자 만들기 ──────────────────────────────────────────────

    GameObject SpawnBox(GameObject prefab, List<Object> temps)
    {
        int layer = SampleBox.ResolveLayer(boxLayerName);

        if (prefab != null)
        {
            var go = Instantiate(prefab);
            go.name = "DatasetBox";
            go.hideFlags = HideFlags.DontSave;
            if (layer >= 0) SampleBox.SetLayerRecursively(go, layer);
            return go;
        }

        return SampleBox.Create(rig.BoxSize, layer, temps);
    }

    string BuildInfo(int images, bool noLabelUsed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("물류 분류 자동화 - FR-03 송장면 판정 학습 이미지");
        sb.AppendLine($"생성 시각 : {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"샘플 수   : {sampleCount} (이미지 {images} 장, 상자 1 개당 5 면)");
        sb.AppendLine($"해상도    : {rig.Resolution} x {rig.Resolution} (FR-03 기준 84 이상)");
        sb.AppendLine($"상자 크기 : {rig.BoxSize.x} x {rig.BoxSize.z} x {rig.BoxSize.y} m (가로 x 세로 x 높이)");
        sb.AppendLine($"난수 시드 : {randomSeed}");
        sb.AppendLine($"방향 비율 : 위 {topRatio:P0} / 바닥 {bottomRatio:P0} / 측면 4 방향 균등");
        sb.AppendLine($"미부착    : {(noLabelUsed ? $"{noLabelRatio:P0}" : "없음")}");
        sb.AppendLine();
        // 아래 네 줄이 다르면 이 데이터셋으로 학습한 모델은 지금 상자에 쓸 수 없다.
        sb.AppendLine("■ 출전 (상자를 바꾸면 반드시 다시 만든다)");
        foreach (var e in Entries())
        {
            sb.AppendLine($"  상자      : {e.Name}  (송장 방향 {e.Normal}, 가중치 {e.weight})");
        }
        sb.AppendLine($"  송장 이름 : {labelChildName}");
        sb.AppendLine($"  레이어    : {boxLayerName}");
        sb.AppendLine();
        sb.AppendLine("labels.csv 열");
        sb.AppendLine("  file      : images/ 아래 파일 경로");
        sb.AppendLine("  sample    : 상자 번호 (같은 번호 5 줄 = 한 상자의 5 면)");
        sb.AppendLine("  face      : 찍은 면 (Top/Right/Back/Left/Front)");
        sb.AppendLine("  has_label : 그 면에 송장이 보이면 1 (면별 이진 분류 정답)");
        sb.AppendLine("  true_face : 그 상자의 실제 송장면 (Bottom, None 포함. 6 분류용 정답)");
        sb.AppendLine("  box       : 그 이미지에 쓰인 상자 Prefab 이름");
        return sb.ToString();
    }

    static void SafeDestroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
