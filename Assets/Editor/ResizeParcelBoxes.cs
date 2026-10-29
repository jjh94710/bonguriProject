using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Bonguri.ParcelTools
{

    public static class ResizeParcelBoxes
    {
        const string Report = "Temp/ParcelResize5x6x7.json";
        static readonly Vector3 Size = new Vector3(.05f, .06f, .07f);
[MenuItem("Tools/Bonguri/Resize Parcels to 5x6x7 cm")]
        public static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/bongyuri.unity") return;
            SessionState.SetBool(Report, true);
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string backup = "Assets/SceneBackups/ResizeParcels/" + stamp + "/bongyuri.unity";
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new Exception("Scene backup failed");
                var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => p.StartsWith("Assets/prefabs/", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Assets/Project/Parcels/", StringComparison.OrdinalIgnoreCase))
                    .Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(p).transform.Find("CardboardBody") != null)
                    .OrderBy(p => PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(p)) == PrefabAssetType.Variant ? 1 : 0).ToArray();
                foreach (string path in paths)
                {
                    string copy = "Backups/ResizeParcels/" + stamp + "/" + path;
                    Directory.CreateDirectory(Path.GetDirectoryName(copy)); File.Copy(path, copy);
                    var contents = PrefabUtility.LoadPrefabContents(path);
                    try { Resize(contents.transform); if (PrefabUtility.SaveAsPrefabAsset(contents, path) == null) throw new Exception("Prefab save failed: " + path); }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                }
                var boxes = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                    .Where(t => t.Find("CardboardBody") != null && t.GetComponent<BoxCollider>() != null).ToArray();
                foreach (var box in boxes) Resize(box);
                AssetDatabase.SaveAssets();
                EditorApplication.delayCall += () => {
                    try
                    {
                        foreach (var box in boxes) Verify(box);
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed");
                        File.WriteAllText(Report, JsonUtility.ToJson(new Result { sceneBoxes = boxes.Length, prefabs = paths.Length, size = Size, rootScale = Vector3.one, verified = true, backup = backup }, true));
                        Debug.Log("Parcel boxes resized to 5 x 6 x 7 cm with root Scale 1,1,1.");
                    }
                    catch (Exception e) { File.WriteAllText(Report, "ERROR: " + e); Debug.LogException(e); }
                };
            }
            catch (Exception e) { File.WriteAllText(Report, "ERROR: " + e); Debug.LogException(e); }
        }
        static void Resize(Transform root)
        {
            var collider = root.GetComponent<BoxCollider>();
            var body = root.Find("CardboardBody");
            if (collider == null || body == null) throw new Exception("Parcel body/collider missing");
            Vector3 old = body.localScale;
            if (old.x <= 0 || old.y <= 0 || old.z <= 0) throw new Exception("Invalid body size");
            Vector3 ratio = new Vector3(Size.x / old.x, Size.y / old.y, Size.z / old.z);
            Undo.RecordObject(root, "Resize parcel");
            root.localScale = Vector3.one; Record(root);
            foreach (Transform child in root)
            {
                Undo.RecordObject(child, "Resize parcel parts");
                child.localPosition = Vector3.Scale(child.localPosition, ratio);
                if (child.name == "ShippingLabel")
                {
                    float uniform = Mathf.Min(ratio.x, Mathf.Min(ratio.y, ratio.z));
                    child.localScale *= uniform;
                }
                else
                {
                    var rotation = child.localRotation;
                    Vector3 axes = new Vector3(Vector3.Scale(rotation * Vector3.right, ratio).magnitude,
                        Vector3.Scale(rotation * Vector3.up, ratio).magnitude, Vector3.Scale(rotation * Vector3.forward, ratio).magnitude);
                    child.localScale = Vector3.Scale(child.localScale, axes);
                }
                Record(child);
            }
            body.localScale = Size; Record(body);
            Undo.RecordObject(collider, "Resize parcel collider"); collider.size = Size; Record(collider);
            Verify(root);
        }
        static void Verify(Transform root)
        {
            if ((root.localScale - Vector3.one).sqrMagnitude > 1e-10f ||
                (root.Find("CardboardBody").localScale - Size).sqrMagnitude > 1e-10f ||
                (root.GetComponent<BoxCollider>().size - Size).sqrMagnitude > 1e-10f)
                throw new Exception("Parcel dimension verification failed: " + root.name);
        }
        static void Record(UnityEngine.Object obj)
        { EditorUtility.SetDirty(obj); if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj); }
        [Serializable] class Result { public int sceneBoxes, prefabs; public Vector3 size, rootScale; public bool verified; public string backup; }
    }
}
