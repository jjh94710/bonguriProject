using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bonguri.ParcelTools
{
    [InitializeOnLoad]
    public static class GroupParcelLabel
    {
        const string PrefabPath = "Assets/Project/Parcels/ParcelBox_6x8x12cm.prefab";
        const string ReportPath = "Temp/ParcelLabelGrouping.json";
        const string SessionKey = "Bonguri.ParcelLabel.Grouped.v1";

        static GroupParcelLabel() { EditorApplication.delayCall += RunOnce; }

        static void RunOnce()
        {
            if (File.Exists(ReportPath) || SessionState.GetBool(SessionKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += RunOnce; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != "Assets/Scenes/bongyuri.unity") return;
            Group();
        }

        [MenuItem("Tools/Bonguri/Group Parcel Shipping Labels")]
        public static void Group()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/bongyuri.unity") return;
            SessionState.SetBool(SessionKey, true);
            try
            {
                var boxes = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Where(t => t.name.StartsWith("ParcelBox_", StringComparison.Ordinal) && t.GetComponent<Rigidbody>() != null).ToArray();
                if (boxes.Length == 0) throw new InvalidOperationException("No parcel box found in active scene");
                string backup = "Assets/SceneBackups/ParcelLabel/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/bongyuri.unity";
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Backup failed");

                var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    GroupChildren(contents.transform, false);
                    if (PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath) == null) throw new IOException("Prefab save failed");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }

                Transform selected = null;
                foreach (var box in boxes)
                {
                    selected = GroupChildren(box, true);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(box);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed");
                AssetDatabase.SaveAssets();
                if (selected != null) Selection.activeGameObject = selected.gameObject;
                var result = new Result {
                    scene = scene.path, prefab = PrefabPath, backup = backup, boxes = boxes.Length,
                    group = selected != null ? selected.name : "",
                    children = selected != null ? selected.Cast<Transform>().Select(t => t.name).ToArray() : Array.Empty<string>()
                };
                File.WriteAllText(ReportPath, JsonUtility.ToJson(result, true));
                Debug.Log("Parcel shipping labels grouped under ShippingLabel; prefab and scene saved.");
            }
            catch (Exception e) { File.WriteAllText(ReportPath, "ERROR: " + e); Debug.LogException(e); }
        }

        static Transform GroupChildren(Transform box, bool undo)
        {
            var existing = box.Find("ShippingLabel");
            if (existing != null && existing.GetComponent<MeshRenderer>() == null) return existing;
            var parts = box.Cast<Transform>().Where(t => t.name == "ShippingLabel" || t.name == "LabelPaper"
                || t.name == "LabelAddressLine" || t.name.StartsWith("LabelBar_", StringComparison.Ordinal)).ToArray();
            if (parts.Length == 0) throw new InvalidOperationException("No label parts found on " + box.name);
            var paper = parts.FirstOrDefault(t => t.name == "ShippingLabel" || t.name == "LabelPaper");
            Vector3 pivot = paper != null ? paper.localPosition : Vector3.zero;
            var group = new GameObject("ShippingLabel");
            if (undo) Undo.RegisterCreatedObjectUndo(group, "Group parcel shipping label");
            group.transform.SetParent(box, false);
            group.transform.localPosition = pivot;
            foreach (var part in parts)
            {
                if (undo) Undo.RecordObject(part.gameObject, "Rename label paper");
                if (part.name == "ShippingLabel") part.name = "LabelPaper";
                if (undo) Undo.SetTransformParent(part, group.transform, "Group label part");
                else part.SetParent(group.transform, true);
                if (undo) PrefabUtility.RecordPrefabInstancePropertyModifications(part);
            }
            return group.transform;
        }

        [Serializable] class Result
        { public string scene, prefab, backup, group; public int boxes; public string[] children; }
    }
}
