using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Bonguri.ParcelTools
{
    /// <summary>
    /// 공정용 상자 Variant 를 만든다.
    ///
    /// BoxInfo 가 적어 둔 방침 그대로다.
    ///   "원본 상자 Prefab(ParcelBox, 봉현님)의 글자는 TextMesh라서 그대로 두고,
    ///    공정용 Variant에서 TextMesh 글자를 끄고 TMP 글자를 새로 둔다 (4단계 LabelLayout)."
    ///
    /// Variant 에서 하는 일
    ///   - 옛 TextMesh 글자(DestinationNumber · DestinationRegion)를 끈다
    ///   - ShippingLabelNumber 를 끈다 (LabelLayout 이 대신 찍는다)
    ///   - TMP 글자 두 개를 새로 둔다. 이름에 Number · Region 이 들어가야
    ///     BoxInfo.Reset 이 찾아 연결한다
    ///   - 바코드를 A.2 의 왼쪽 위로 옮긴다
    ///   - BoxInfo 를 달고 label · numberText · regionText 를 연결한다
    ///
    /// 원본 Prefab 은 건드리지 않는다. Variant 만 새로 만든다.
    /// </summary>
    public static class MakeProcessBoxVariants
    {
        const string OutDir = "Assets/Prefabs/Process";
        const string Report = "Temp/ProcessBoxVariants.txt";

        static readonly string[] Bases =
        {
            "Assets/Project/Parcels/ParcelBox_6x8x12cm.prefab",
            "Assets/Prefabs/ParcelBox_GraspTest_horizontal.prefab",
            "Assets/Prefabs/ParcelBox_GraspTest_vertical.prefab",
        };

        [MenuItem("Tools/Bonguri/Make Process Box Variants")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Variant] Play 중에는 돌리지 않는다.");
                return;
            }

            var layout = Layouts();
            if (layout == null)
            {
                Debug.LogError("[Variant] LabelLayout 에셋이 없다. "
                             + "Project 창에서 Create → Factory → Label Layout 으로 먼저 만든다.");
                return;
            }
            if (layout.font == null)
            {
                Debug.LogError("[Variant] LabelLayout 의 font 가 비어 있다. "
                             + "TMP 글꼴 에셋을 넣고 다시 돌린다.", layout);
                return;
            }

            Directory.CreateDirectory(OutDir);
            var lines = new System.Collections.Generic.List<string>();

            try
            {
                foreach (string basePath in Bases)
                {
                    if (!File.Exists(basePath)) continue;
                    lines.Add(Build(basePath, layout));
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                File.WriteAllText(Report, string.Join("\n", lines));
                Debug.Log($"[Variant] 공정용 Variant {lines.Count} 개를 만들었다.\n"
                        + string.Join("\n", lines));
            }
            catch (Exception e)
            {
                File.WriteAllText(Report, "ERROR: " + e);
                Debug.LogException(e);
            }
        }

        static LabelLayout Layouts()
        {
            var guids = AssetDatabase.FindAssets("t:LabelLayout");
            if (guids.Length == 0) return null;
            if (guids.Length > 1)
                Debug.LogWarning($"[Variant] LabelLayout 에셋이 {guids.Length} 개다. "
                               + "찍는 쪽과 읽는 쪽이 같은 것을 써야 하므로 하나로 둔다.");
            return AssetDatabase.LoadAssetAtPath<LabelLayout>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        static string Build(string basePath, LabelLayout layout)
        {
            var baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            if (baseAsset == null) throw new Exception("원본을 못 읽는다: " + basePath);

            string outPath = $"{OutDir}/{Path.GetFileNameWithoutExtension(basePath)}_Process.prefab";

            // 열려 있는 씬을 건드리지 않도록 미리보기 씬에서 작업한다.
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset, preview);
                if (instance == null) throw new Exception("인스턴스를 못 만든다: " + basePath);

                var label = instance.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == "ShippingLabel" && t.Find("LabelPaper") != null);
                if (label == null) throw new Exception("ShippingLabel 이 없다: " + basePath);

                Convert(label, layout, out float width, out float height);

                var info = instance.GetComponent<BoxInfo>();
                if (info == null) info = instance.AddComponent<BoxInfo>();
                Wire(info, label);

                // 검산은 저장 전에 인스턴스로 한다. 프리팹 에셋 위의 TMP 는 씬에 없어서
                // ForceMeshUpdate 가 글자를 그리지 않는다.
                Verify(instance, layout);

                // 프리팹 인스턴스를 저장하면 Variant 가 된다 (복사본이 아니다).
                var saved = PrefabUtility.SaveAsPrefabAsset(instance, outPath);
                if (saved == null) throw new Exception("Variant 저장 실패: " + outPath);
                return $"  {outPath}\n      송장 {width * 100:F2} x {height * 100:F2} cm, "
                     + $"TMP 글자 2 개, BoxInfo 연결 완료";
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        /// <summary>Variant 안에서 옛 글자를 끄고 TMP 글자를 두고 바코드를 옮긴다.</summary>
        static void Convert(Transform label, LabelLayout layout, out float width, out float height)
        {
            var paper = label.Find("LabelPaper");
            var legacy = label.Find("DestinationNumber");
            if (legacy == null) throw new Exception("DestinationNumber 가 없다: " + label.name);

            // 글자가 눕는 틀은 옛 글자의 회전을 그대로 쓴다.
            Quaternion rotation = legacy.localRotation;
            Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up;

            width = Mathf.Abs(right.x) * paper.localScale.x + Mathf.Abs(right.z) * paper.localScale.z;
            height = Mathf.Abs(up.x) * paper.localScale.x + Mathf.Abs(up.z) * paper.localScale.z;
            float lift = paper.localScale.y * .5f + .00005f;

            // 옛 TextMesh 글자와 그걸 몰던 컴포넌트를 끈다. 지우지 않는 이유는
            // 원본 Prefab 에 그대로 있어야 봉현님 쪽 씬이 안 깨지기 때문이다.
            foreach (string n in new[] { "DestinationNumber", "DestinationRegion" })
            {
                var t = label.Find(n);
                var r = t != null ? t.GetComponent<MeshRenderer>() : null;
                if (r != null) r.enabled = false;
            }
            var old = label.GetComponent<ShippingLabelNumber>();
            if (old != null) old.enabled = false;

            // TMP 글자 두 개. 이름에 Number · Region 이 들어가야 BoxInfo.Reset 이 찾는다.
            MakeText(label, "DestinationNumberTMP", layout, rotation, paper.localPosition, lift);
            MakeText(label, "DestinationRegionTMP", layout, rotation, paper.localPosition, lift);

            // 바코드를 A.2 의 왼쪽 위로.
            var bars = label.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("LabelBar_", StringComparison.Ordinal))
                .OrderBy(t => int.Parse(t.name.Substring(9))).ToArray();

            if (bars.Length > 0)
            {
                Rect cell = layout.barcodeCell;
                float gap = .00065f;
                float total = bars.Sum(t => t.localScale.x) + (bars.Length - 1) * gap;
                float factor = width * cell.width / total;
                float cursor = width * (cell.xMin - .5f);
                float v = height * (cell.center.y - .5f);

                foreach (var bar in bars)
                {
                    float barWidth = bar.localScale.x * factor;
                    bar.SetParent(label, false);
                    bar.localRotation = Quaternion.identity;
                    bar.localPosition = paper.localPosition + Vector3.up * (paper.localScale.y * .5f + .00015f)
                                      + right * (cursor + barWidth * .5f) + up * v;
                    bar.localScale = new Vector3(barWidth, .0002f, height * cell.height);
                    cursor += barWidth + gap * factor;
                }
            }

            // 주소 줄은 지역명 칸 아래로.
            var line = label.Find("LabelAddressLine");
            if (line != null)
            {
                line.localPosition = paper.localPosition + Vector3.up * (paper.localScale.y * .5f + .00015f)
                                   + up * (height * (layout.regionCell.yMin - .5f - .04f));
                line.localScale = new Vector3(width * .80f, .00015f, height * .008f);
            }
        }

        static void MakeText(Transform label, string name, LabelLayout layout,
                             Quaternion rotation, Vector3 paperPos, float lift)
        {
            var existing = label.Find(name);
            GameObject go;

            if (existing != null) go = existing.gameObject;
            else
            {
                // RectTransform 을 처음부터 달고 만든다. TextMeshPro 는 RectTransform 을
                // 요구해서, 평범한 Transform 위에 붙이면 Unity 가 Transform 을 갈아끼우며
                // 옛 것을 파괴한다. 그 Transform 을 들고 있으면 참조가 끊긴다.
                go = new GameObject(name, typeof(RectTransform));

                // new GameObject 는 활성 씬에 생긴다. 미리보기 씬으로 먼저 옮겨야
                // SetParent 가 된다. 씬을 넘나드는 부모 연결은 Unity 가 거부한다.
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                    go, label.gameObject.scene);
                go.transform.SetParent(label, false);
            }

            if (go == null) throw new Exception($"{name} 을 만들지 못했다.");

            var tmp = go.GetComponent<TextMeshPro>();
            if (tmp == null) tmp = go.AddComponent<TextMeshPro>();

            tmp.font = layout.font;
            tmp.color = Color.black;
            tmp.text = string.Empty;

            // Transform 참조는 컴포넌트를 다 붙인 뒤에 얻는다. GameObject 는 그대로지만
            // Transform 은 위 이유로 바뀌어 있을 수 있다.
            var rt = tmp.rectTransform;

            // 가로 · 세로 자리는 LabelLayout.Print 가 잡는다. 여기서는 종이 위로 띄우는
            // 높이와 눕는 방향만 정한다. RectTransform 이므로 localPosition 이 아니라
            // anchoredPosition3D 로 넣어야 값이 남는다.
            rt.localRotation = rotation;
            rt.anchoredPosition3D = paperPos + Vector3.up * lift;
        }

        static void Wire(BoxInfo info, Transform label)
        {
            info.label = label;
            foreach (var tmp in label.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (info.numberText == null && tmp.name.Contains("Number")) info.numberText = tmp;
                if (info.regionText == null && tmp.name.Contains("Region")) info.regionText = tmp;
            }
        }

        static void Verify(GameObject variant, LabelLayout layout)
        {
            var info = variant.GetComponent<BoxInfo>();
            if (info == null) throw new Exception("BoxInfo 가 없다: " + variant.name);
            if (info.label == null) throw new Exception("BoxInfo.label 이 비었다: " + variant.name);
            if (info.numberText == null) throw new Exception("BoxInfo.numberText 가 비었다: " + variant.name);
            if (info.regionText == null) throw new Exception("BoxInfo.regionText 가 비었다: " + variant.name);
            if (info.numberText == info.regionText)
                throw new Exception("numberText 와 regionText 가 같은 오브젝트다: " + variant.name);

            // 옛 글자가 꺼졌는지.
            foreach (string n in new[] { "DestinationNumber", "DestinationRegion" })
            {
                var t = info.label.Find(n);
                var r = t != null ? t.GetComponent<MeshRenderer>() : null;
                if (r != null && r.enabled)
                    throw new Exception($"옛 글자 {n} 이 아직 켜져 있다: {variant.name}");
            }

            // 실제로 찍어 보고 글자가 칸 안에 드는지.
            layout.Print(info, "22137", "대전");
            if (info.labelNumber != "22137" || info.labelRegion != "대전")
                throw new Exception("Print 가 정답을 기록하지 않았다: " + variant.name);
            if (info.numberText.text.Length == 0)
                throw new Exception("번호가 찍히지 않았다: " + variant.name);
        }
    }
}
