using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bonguri.ParcelTools
{

    public static class SetupShippingLabelRegions
    {
        const string Report = "Temp/ShippingLabelRegions-Recovery.json";
        const string FontPath = "Assets/Project/Parcels/Fonts/NotoSansKR-Regular.otf";
        static readonly string[] Paths = {
            "Assets/Project/Parcels/ParcelBox_6x8x12cm.prefab", "Assets/Prefabs/box_prefabs.prefab",
            "Assets/Prefabs/ParcelBox_GraspTest_horizontal.prefab", "Assets/Prefabs/ParcelBox_GraspTest_vertical.prefab" };
[MenuItem("Tools/Bonguri/Arrange Shipping Label Regions")]
        public static void Setup()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/bongyuri.unity") return;
            SessionState.SetBool(Report, true);
            try
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
                if (font == null) throw new Exception("Korean font missing");
                font.RequestCharactersInTexture("102030서울특별시경기도충청도", 80, FontStyle.Normal);
                foreach (char c in "서울특별시경기도충청도") if (!font.HasCharacter(c)) throw new Exception("Missing Korean glyph: " + c);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string backup = "Assets/SceneBackups/LabelRegions/" + stamp + "/bongyuri.unity";
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new Exception("Scene backup failed");
                int prefabs = 0;
                foreach (string path in Paths)
                {
                    if (!File.Exists(path)) continue;
                    string copy = "Backups/LabelRegions/" + stamp + "/" + path;
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)); File.Copy(path, copy);
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        foreach (var label in Labels(root)) Configure(label, font);
                        if (PrefabUtility.SaveAsPrefabAsset(root, path) == null) throw new Exception("Prefab save failed: " + path);
                        prefabs++;
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                var labels = scene.GetRootGameObjects().SelectMany(Labels).ToArray();
                foreach (var label in labels) Configure(label, font);
                AssetDatabase.SaveAssets();
                EditorApplication.delayCall += () => Finish(scene, labels, backup, prefabs);
            }
            catch (Exception e) { File.WriteAllText(Report, "ERROR: " + e); Debug.LogException(e); }
        }
        static Transform[] Labels(GameObject root) => root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == "ShippingLabel" && t.Find("LabelPaper") != null).ToArray();
        static void Configure(Transform label, Font font)
        {
            var paper = label.Find("LabelPaper");
            var controller = label.GetComponent<ShippingLabelNumber>(); if (controller == null) controller = label.gameObject.AddComponent<ShippingLabelNumber>();
            var number = label.Find("DestinationNumber")?.GetComponent<TextMesh>();
            if (number == null) throw new Exception("DestinationNumber missing on " + label.name);
            var rotation = number.transform.localRotation;
            Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up;
            float width = Mathf.Abs(right.x) * paper.localScale.x + Mathf.Abs(right.z) * paper.localScale.z;
            float height = Mathf.Abs(up.x) * paper.localScale.x + Mathf.Abs(up.z) * paper.localScale.z;
            Vector3 center = paper.localPosition + Vector3.up * (paper.localScale.y * .5f + .00005f);
            var regionTransform = label.Find("DestinationRegion");
            if (regionTransform == null) { regionTransform = new GameObject("DestinationRegion").transform; regionTransform.SetParent(label, false); }
            var region = regionTransform.GetComponent<TextMesh>(); if (region == null) region = regionTransform.gameObject.AddComponent<TextMesh>();
            foreach (var text in new[] { number, region })
            {
                text.font = font; text.fontSize = 80; text.characterSize = .01f;
                text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
                text.fontStyle = FontStyle.Normal; text.color = Color.black;
                text.transform.localRotation = rotation;
                text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            number.transform.localPosition = center + up * height * .31f;
            region.transform.localPosition = center + up * height * .075f;
            controller.Configure(number, width * .75f, height * .20f);
            controller.ConfigureRegion(region, width * .85f, height * .105f);
            var bars = label.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("LabelBar_", StringComparison.Ordinal)).OrderBy(t => int.Parse(t.name.Substring(9))).ToArray();
            if (bars.Length > 0)
            {
                // Keep original bar width ratios while fitting a compact barcode in the readable lower-left corner.
                float total = bars.Sum(t => t.localScale.x) + (bars.Length - 1) * .00065f;
                float factor = width * .34f / total;
                float cursor = -width * .41f;
                foreach (var bar in bars)
                {
                    float barWidth = bar.localScale.x * factor;
                    bar.SetParent(label, false);
                    bar.localRotation = Quaternion.identity;
                    bar.localPosition = paper.localPosition + Vector3.up * (paper.localScale.y * .5f + .00015f) + right * (cursor + barWidth * .5f) - up * height * .34f;
                    // All current label papers and barcode bars lie in their parent's XZ plane.
                    bar.localScale = new Vector3(barWidth, .0002f, height * .12f);
                    cursor += barWidth + .00065f * factor;
                    Record(bar);
                }
            }
            var line = label.Find("LabelAddressLine");
            if (line != null) { line.localPosition = paper.localPosition + Vector3.up * (paper.localScale.y * .5f + .00015f) - up * height * .095f; line.localScale = new Vector3(width * .80f, .00015f, height * .008f); Record(line); }
            int original = controller.Code;
            foreach (int code in new[] { 10, 20, 30 })
            {
                controller.SetCode(code);
                string expected = code == 10 ? "서울특별시" : code == 20 ? "경기도" : "충청도";
                if (region.text != expected || number.text != code.ToString()) throw new Exception("Label mapping failed");
            }
            controller.SetCode(original);
            foreach (var item in new UnityEngine.Object[] { controller, number, number.transform, region, region.transform, number.GetComponent<MeshRenderer>(), region.GetComponent<MeshRenderer>() }) Record(item);
        }
        static void Record(UnityEngine.Object obj)
        { EditorUtility.SetDirty(obj); if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj); }
        static void Finish(Scene scene, Transform[] labels, string backup, int prefabs)
        {
            try
            {
                foreach (var label in labels) label.GetComponent<ShippingLabelNumber>().Refresh();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed");
                File.WriteAllText(Report, JsonUtility.ToJson(new Result { labels = labels.Length, prefabs = prefabs, scene = scene.path, backup = backup, mappingsVerified = true, koreanGlyphsVerified = true }, true));
                Debug.Log("Shipping label regions and lower-left barcodes saved.");
            }
            catch (Exception e) { File.WriteAllText(Report, "ERROR: " + e); Debug.LogException(e); }
        }
        [Serializable] class Result { public int labels, prefabs; public string scene, backup; public bool mappingsVerified, koreanGlyphsVerified; }
    }
}
