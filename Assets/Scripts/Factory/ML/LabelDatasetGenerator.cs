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
    [Header("연결")]
    [SerializeField] FaceCameraRig rig;

    [Tooltip("상자 Prefab. 비우면 임시 상자를 만들어 쓴다(문강호 송장 텍스처가 나오면 교체).")]
    [SerializeField] GameObject boxPrefab;

    [Tooltip("상자 기준에서 송장이 붙은 면의 방향. 임시 상자는 +Z 에 붙인다.")]
    [SerializeField] Vector3 labelLocalNormal = Vector3.forward;

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

        var rng = new System.Random(randomSeed);
        var csv = new StringBuilder();
        csv.AppendLine("file,sample,face,has_label,true_face");

        GameObject box = null;
        var temps = new List<Object>();
        int written = 0;

        try
        {
            box = SpawnBox(temps);
            Renderer labelRenderer = SampleBox.FindLabel(box, labelChildName);

            // 송장 오브젝트를 못 찾으면 끌 수가 없다. 그대로 두면 송장이 붙은 상자를
            // "미부착"으로 적어 라벨이 틀어지므로, 미부착 샘플 자체를 빼고 만든다.
            bool makeNoLabel = includeNoLabelSamples && labelRenderer != null;
            if (includeNoLabelSamples && labelRenderer == null)
            {
                Debug.LogWarning(
                    $"[LabelDataset] 상자에서 송장 오브젝트('{labelChildName}')를 못 찾았다. " +
                    "송장 미부착 샘플은 빼고 생성한다. Label Child Name 을 실제 이름에 맞춘 뒤 다시 만든다.", this);
            }

            for (int s = 0; s < sampleCount; s++)
            {
                bool noLabel = makeNoLabel && rng.NextDouble() < noLabelRatio;
                if (labelRenderer != null) labelRenderer.enabled = !noLabel;

                BoxFace target = noLabel ? BoxFace.None : PickFace(rng);
                PlaceBox(box.transform, target, rng);

                // 실제 자세에서 송장이 어느 면에 있는지 다시 계산한다(배치가 틀려도 정답은 맞게).
                BoxFace trueFace = noLabel ? BoxFace.None : ActualFace(box.transform);

                Texture2D[] shots = rig.Capture();
                for (int i = 0; i < FaceCameraRig.FaceCount; i++)
                {
                    if (shots[i] == null) continue;

                    BoxFace face = FaceCameraRig.CapturedFaces[i];
                    string name = string.Format(CultureInfo.InvariantCulture, "{0:D4}_{1}.png", s, face);
                    File.WriteAllBytes(Path.Combine(imageDir, name), shots[i].EncodeToPNG());

                    int hasLabel = (face == trueFace) ? 1 : 0;
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "images/{0},{1},{2},{3},{4}", name, s, face, hasLabel, trueFace));
                    written++;
                }
                FaceCameraRig.ReleaseShots(shots);
            }

            File.WriteAllText(Path.Combine(root, "labels.csv"), csv.ToString(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(root, "dataset_info.txt"), BuildInfo(written, makeNoLabel), Encoding.UTF8);
        }
        finally
        {
            foreach (var t in temps) SafeDestroy(t);
            if (box != null) SafeDestroy(box);
        }

        Debug.Log($"[LabelDataset] 이미지 {written} 장 생성 → {root}");
        return written;
    }

    // ── 면 고르기 / 상자 세우기 ──────────────────────────────────

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

    void PlaceBox(Transform box, BoxFace target, System.Random rng)
    {
        Vector3 wanted = (target == BoxFace.None)
            ? FaceCameraRig.OutwardOf(BoxFace.Top)
            : FaceCameraRig.OutwardOf(target);

        Vector3 local = labelLocalNormal.sqrMagnitude < 1e-6f ? Vector3.forward : labelLocalNormal.normalized;
        Quaternion baseRot = Quaternion.FromToRotation(local, wanted);

        float spin = 0f;
        if (quarterTurnJitter) spin = rng.Next(0, 4) * 90f;
        spin += (float)(rng.NextDouble() * 2.0 - 1.0) * angleJitter;

        box.rotation = Quaternion.AngleAxis(spin, wanted) * baseRot;

        Vector3 jitter = new Vector3(
            (float)(rng.NextDouble() * 2.0 - 1.0),
            0f,
            (float)(rng.NextDouble() * 2.0 - 1.0)) * positionJitter;
        box.position = rig.BoxCenter + jitter;
    }

    /// <summary>지금 자세에서 송장면이 향하는 월드 방향에 가장 가까운 면.</summary>
    BoxFace ActualFace(Transform box)
    {
        Vector3 local = labelLocalNormal.sqrMagnitude < 1e-6f ? Vector3.forward : labelLocalNormal.normalized;
        Vector3 n = box.TransformDirection(local).normalized;

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

    GameObject SpawnBox(List<Object> temps)
    {
        int layer = SampleBox.ResolveLayer(boxLayerName);

        if (boxPrefab != null)
        {
            var go = Instantiate(boxPrefab);
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
        sb.AppendLine($"  상자      : {(boxPrefab != null ? boxPrefab.name : "임시 상자 (SampleBox) - 조원 Prefab 으로 교체 필요")}");
        sb.AppendLine($"  송장 이름 : {labelChildName}");
        sb.AppendLine($"  송장 방향 : 상자 로컬 {labelLocalNormal}");
        sb.AppendLine($"  레이어    : {boxLayerName}");
        sb.AppendLine();
        sb.AppendLine("labels.csv 열");
        sb.AppendLine("  file      : images/ 아래 파일 경로");
        sb.AppendLine("  sample    : 상자 번호 (같은 번호 5 줄 = 한 상자의 5 면)");
        sb.AppendLine("  face      : 찍은 면 (Top/Right/Back/Left/Front)");
        sb.AppendLine("  has_label : 그 면에 송장이 보이면 1 (면별 이진 분류 정답)");
        sb.AppendLine("  true_face : 그 상자의 실제 송장면 (Bottom, None 포함. 6 분류용 정답)");
        return sb.ToString();
    }

    static void SafeDestroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
