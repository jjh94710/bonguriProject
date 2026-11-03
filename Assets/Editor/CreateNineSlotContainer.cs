using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CreateNineSlotContainer
{
    static CreateNineSlotContainer() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/CreateNineSlotContainer.request")) return;
        File.Delete("Temp/CreateNineSlotContainer.request"); Create();
    }
    [MenuItem("Tools/Bonguri/Create 3x3 Parcel Container")]
    public static void Create()
    {
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var old = GameObject.Find("ParcelContainer_2x3_6");
            if (old == null) throw new Exception("Existing 2x3 container not found.");
            var scene = old.scene;
            string backup = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "/UnityBackups/bonguriProject/NineSlotContainer/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/bongyuri.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new Exception("Scene backup failed.");
            const string path = "Assets/prefabs/ParcelContainer_3x3_9.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null || GameObject.Find("ParcelContainer_3x3_9") != null) throw new Exception("3x3 container already exists.");
            var parcel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/ParcelBox_ProcessVariant.prefab");
            var box = parcel.GetComponent<BoxCollider>();
            Vector3 size = Vector3.Scale(box.size, parcel.transform.localScale);
            const float thickness = .012f, margin = .01f;
            float pitchX = size.x + margin, pitchZ = size.z + margin;
            float innerX = 3 * pitchX + .02f, innerZ = 3 * pitchZ + .02f;
            float height = Mathf.Max(.08f, size.y + .02f);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/prefabs/ParcelStackContainer_Material.mat");
            var root = new GameObject("ParcelContainer_3x3_9");
            Part(root, "Floor", new Vector3(0, thickness / 2, 0), new Vector3(innerX + thickness * 2, thickness, innerZ + thickness * 2), material);
            Part(root, "LeftWall", new Vector3(-(innerX + thickness) / 2, thickness + height / 2, 0), new Vector3(thickness, height, innerZ + thickness * 2), material);
            Part(root, "RightWall", new Vector3((innerX + thickness) / 2, thickness + height / 2, 0), new Vector3(thickness, height, innerZ + thickness * 2), material);
            Part(root, "FrontWall", new Vector3(0, thickness + height / 2, -(innerZ + thickness) / 2), new Vector3(innerX, height, thickness), material);
            Part(root, "BackWall", new Vector3(0, thickness + height / 2, (innerZ + thickness) / 2), new Vector3(innerX, height, thickness), material);
            var slotsRoot = new GameObject("Slots").transform; slotsRoot.SetParent(root.transform, false);
            for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++)
            {
                var slot = new GameObject("Slot_" + (z * 3 + x + 1)).transform;
                slot.SetParent(slotsRoot, false); slot.localPosition = new Vector3((x - 1) * pitchX, thickness + size.y / 2, (z - 1) * pitchZ);
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path); UnityEngine.Object.DestroyImmediate(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Create 3x3 container");
            instance.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
            instance.transform.localScale = Vector3.one;
            var slots = instance.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Slot_")).OrderBy(t => t.name).ToArray();
            foreach (var loader in UnityEngine.Object.FindObjectsByType<BoxAttacher>().Where(a => a.container == old.transform))
            {
                Undo.RecordObject(loader, "Connect nine container slots"); loader.container = instance.transform; loader.slots = slots;
                EditorUtility.SetDirty(loader); PrefabUtility.RecordPrefabInstancePropertyModifications(loader);
            }
            Undo.RecordObject(old, "Keep old container as inactive backup"); old.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = instance;
            SceneView.lastActiveSceneView?.Frame(new Bounds(instance.transform.position + Vector3.up * .04f, Vector3.one * .6f), false);
            File.WriteAllText("Temp/NineSlotContainerResult.txt", "OK slots=" + slots.Length + " colliders=" + instance.GetComponentsInChildren<Collider>().Length + " boxSize=" + size + " inner=" + innerX + "x" + innerZ + " pitch=" + pitchX + "x" + pitchZ + " prefab=" + path + " backup=" + backup);
        }
        catch (Exception e) { File.WriteAllText("Temp/NineSlotContainerResult.txt", "ERROR " + e); Debug.LogException(e); }
    }
    static void Part(GameObject parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = name; part.transform.SetParent(parent.transform, false);
        part.transform.localPosition = position; part.transform.localScale = size; part.GetComponent<Renderer>().sharedMaterial = material;
    }
}
