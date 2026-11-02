using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class TotoSetup
{
    static TotoSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/TotoSetup.request")) return;
        File.Delete("Temp/TotoSetup.request"); Setup();
    }
    [MenuItem("Tools/Bonguri/Configure TotoBase 2x2x2")]
    public static void Setup()
    {
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var loader = UnityEngine.Object.FindObjectsByType<BoxAttacher>().Single(a => a.name == "fr5_suction (1)");
            var old = loader.container != null ? loader.container.gameObject : GameObject.Find("ParcelContainer_3x3_9");
            if (old == null) throw new Exception("Existing container missing.");
            var scene = old.scene;
            string backup = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "/UnityBackups/bonguriProject/TotoBase/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/bongyuri.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new Exception("Backup failed.");
            const string path = "Assets/prefabs/ParcelContainer_2x2x2_8.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var root = new GameObject("ParcelContainer_2x2x2_8");
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/prefabs/ParcelStackContainer_Material.mat");
                Part(root, "Floor", new Vector3(0, .006f, 0), new Vector3(.154f, .012f, .174f), material);
                Part(root, "LeftWall", new Vector3(-.071f, .092f, 0), new Vector3(.012f, .16f, .174f), material);
                Part(root, "RightWall", new Vector3(.071f, .092f, 0), new Vector3(.012f, .16f, .174f), material);
                Part(root, "FrontWall", new Vector3(0, .092f, -.081f), new Vector3(.13f, .16f, .012f), material);
                Part(root, "BackWall", new Vector3(0, .092f, .081f), new Vector3(.13f, .16f, .012f), material);
                var tote = root.AddComponent<TotoBase>();
                var slotRoot = new GameObject("Slots").transform; slotRoot.SetParent(root.transform, false);
                for (int layer = 0; layer < 2; layer++) for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
                {
                    int index = layer * 4 + z * 2 + x;
                    var slot = new GameObject("Slot_" + (index + 1)).transform; slot.SetParent(slotRoot, false);
                    slot.localPosition = new Vector3((x - .5f) * .06f, .047f + layer * .07f, (z - .5f) * .07f);
                    tote.slots[index] = slot;
                }
                prefab = PrefabUtility.SaveAsPrefabAsset(root, path); UnityEngine.Object.DestroyImmediate(root);
            }
            var instance = GameObject.Find("ParcelContainer_2x2x2_8");
            if (instance == null) { instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); Undo.RegisterCreatedObjectUndo(instance, "Create eight-box tote"); }
            instance.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
            var toto = instance.GetComponent<TotoBase>(); Undo.RecordObject(toto, "Connect tote loader"); toto.loader = loader;
            Undo.RecordObject(loader, "Connect two-layer tote"); loader.toto = toto; loader.container = instance.transform; loader.slots = toto.slots;
            if (loader.targetBox == null)
                loader.targetBox = UnityEngine.Object.FindObjectsByType<BoxCollider>().Where(b => b.attachedRigidbody != null && b.transform.Find("CardboardBody") != null)
                    .OrderBy(b => Vector3.Distance(b.bounds.center, loader.tcp.position)).FirstOrDefault();
            if (loader.targetBox == null) throw new Exception("Pickup parcel missing.");
            if (old != instance) { Undo.RecordObject(old, "Hide previous tote"); old.SetActive(false); }
            EditorUtility.SetDirty(loader); EditorUtility.SetDirty(toto);
            PrefabUtility.RecordPrefabInstancePropertyModifications(loader); PrefabUtility.RecordPrefabInstancePropertyModifications(toto);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = instance; SceneView.lastActiveSceneView?.Frame(new Bounds(instance.transform.position + Vector3.up * .08f, Vector3.one * .6f), false);
            File.WriteAllText("Temp/TotoSetupResult.txt", "OK slots=" + toto.slots.Length + " capacity=" + toto.Capacity + " colliders=" + instance.GetComponentsInChildren<Collider>().Length + " pickup=" + loader.targetBox.bounds.center + " backup=" + backup);
        }
        catch (Exception e) { File.WriteAllText("Temp/TotoSetupResult.txt", "ERROR " + e); Debug.LogException(e); }
    }
    static void Part(GameObject root, string name, Vector3 position, Vector3 size, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = name; part.transform.SetParent(root.transform, false);
        part.transform.localPosition = position; part.transform.localScale = size; part.GetComponent<Renderer>().sharedMaterial = material;
    }
}
