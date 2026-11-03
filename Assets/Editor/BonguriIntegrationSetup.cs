using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BonguriIntegrationSetup
{
    static BonguriIntegrationSetup() { EditorApplication.update += Poll; if (!string.IsNullOrEmpty(SessionState.GetString("BonguriIntegration.Run", ""))) EditorApplication.update += StartRequestedRun; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string request = "Temp/BonguriIntegration.request";
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        try
        {
            if (command == "audit") Audit();
            if (command == "setup") Setup();
            if (command == "connect-totes") ConnectTerminalTotes();
            if (command == "connect-loading") ConnectTerminalLoading();
            if (command == "run-loading-check") BonguriTerminalLoadingCheck.Run();
            if (command == "run-pickup-check") BonguriPickupCheck.Run();
            if (command == "run-pickup-x-check") BonguriPickupCheck.Run(true);
            if (command == "run-route-check") BonguriTerminalLoadingCheck.RunRouteCheck();
            if (command == "audit-belt-paths") BonguriConveyorSurfaceSetup.Audit();
            if (command == "connect-belt-paths") BonguriConveyorSurfaceSetup.Connect();
            if (command == "run-surface-check") BonguriConveyorSurfaceCheck.Run();
            if (command == "repair-b-entry") BonguriConveyorSurfaceSetup.RepairBEntry();
            if (command == "audit-loading-layout") MatchTerminalLayout(false);
            if (command == "match-loading-layout") MatchTerminalLayout(true);
            if (command == "run-top" || command == "run-side" || command == "run-random")
            {
                SessionState.SetString("BonguriIntegration.Run", command);
                EditorApplication.isPlaying = true;
                EditorApplication.update += StartRequestedRun;
            }
        }
        catch (Exception e) { File.WriteAllText("Temp/BonguriIntegration.error.txt", e.ToString()); Debug.LogException(e); }
    }
    static void StartRequestedRun()
    {
        if (!EditorApplication.isPlaying || Time.time < 1) return;
        EditorApplication.update -= StartRequestedRun;
        var panel = UnityEngine.Object.FindFirstObjectByType<BonguriIntegrationPanel>();
        if (panel != null)
        {
            string command = SessionState.GetString("BonguriIntegration.Run", "");
            if (command == "run-random") panel.RunRandomDemo();
            else panel.RunSmoke(command == "run-side");
        }
        SessionState.EraseString("BonguriIntegration.Run");
    }
    [MenuItem("Tools/Bonguri/Integration/Match A B Loading Layout To C")]
    public static void MatchTerminalLayout() => MatchTerminalLayout(true);
    static void MatchTerminalLayout(bool apply)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play first.");
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/bongyuri.unity")throw new InvalidOperationException("Open bongyuri first.");
        var all=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
        var layouts=new[]{"A","B","C"}.Select(id=>{
            var group=all.Single(t=>t.name==id+"PartRobot");
            var loader=group.GetComponentsInChildren<BoxAttacher>(true).Single();
            var line=all.Single(t=>t.name=="Terminal"+id).GetComponent<ConveyorLine>();
            var edit=new SerializedObject(line);var belts=edit.FindProperty("beltSurfaces");
            var last=(Collider)belts.GetArrayElementAtIndex(belts.arraySize-1).objectReferenceValue;
            if(loader.toto==null||line.StopPoint==null||last==null)throw new InvalidOperationException(id+" missing loading layout.");
            var frame=Quaternion.LookRotation(Vector3.ProjectOnPlane(-last.transform.forward,Vector3.up).normalized,Vector3.up);
            return new {id,group,loader,line,frame,tote=loader.toto.transform};
        }).ToArray();
        var c=layouts[2];var report=new StringBuilder("Match loading layout to current C\n");
        foreach(var p in layouts)
            report.AppendLine(p.id+" before: stop="+p.line.StopPoint.position.ToString("F4")+" robot="+p.loader.transform.position.ToString("F4")+" rotation="+p.loader.transform.eulerAngles.ToString("F3")+" scale="+p.loader.transform.lossyScale.ToString("F3")+" tote="+p.tote.position.ToString("F4")+" relativeRobot="+(Quaternion.Inverse(p.frame)*(p.loader.transform.position-p.line.StopPoint.position)).ToString("F4"));
        if(apply)
        {
            Directory.CreateDirectory("Temp/BonguriIntegrationBackup");
            string backup="Temp/BonguriIntegrationBackup/before_match_loading_C_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity";
            if(!EditorSceneManager.SaveScene(scene,backup,true))throw new IOException("Backup failed.");
            report.AppendLine("Backup: "+backup);
            Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Match A B loading layout to C");
            foreach(var p in layouts.Take(2))
            {
                Quaternion basis=p.frame*Quaternion.Inverse(c.frame);
                Vector3 desiredRobot=p.line.StopPoint.position+basis*(c.loader.transform.position-c.line.StopPoint.position);
                Quaternion desiredRotation=basis*c.loader.transform.rotation;
                Quaternion delta=desiredRotation*Quaternion.Inverse(p.loader.transform.rotation);
                Vector3 desiredGroup=desiredRobot+delta*(p.group.position-p.loader.transform.position);
                Vector3 desiredTote=p.line.StopPoint.position+basis*(c.tote.position-c.line.StopPoint.position);
                Quaternion toteRotation=basis*c.tote.rotation;
                Undo.RecordObjects(new UnityEngine.Object[]{p.group,p.tote},"Match robot base and tote to C");
                p.group.SetPositionAndRotation(desiredGroup,delta*p.group.rotation);
                p.tote.SetPositionAndRotation(desiredTote,toteRotation);
                PrefabUtility.RecordPrefabInstancePropertyModifications(p.group);PrefabUtility.RecordPrefabInstancePropertyModifications(p.tote);
                if(Vector3.Distance(p.loader.transform.position,desiredRobot)>.001f||Quaternion.Angle(p.loader.transform.rotation,desiredRotation)>.1f)throw new InvalidOperationException(p.id+" layout verification failed.");
                report.AppendLine(p.id+" after: robot="+p.loader.transform.position.ToString("F4")+" tote="+p.tote.position.ToString("F4")+" relativeRobot="+(Quaternion.Inverse(p.frame)*(p.loader.transform.position-p.line.StopPoint.position)).ToString("F4"));
            }
            Physics.SyncTransforms();Undo.CollapseUndoOperations(undo);EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed.");
        }
        File.WriteAllText("Temp/BonguriLoadingLayout.txt",report.ToString());Debug.Log("[Loading Layout] "+report);
    }
    [MenuItem("Tools/Bonguri/Integration/Connect Terminal Totes A B C")]
    public static void ConnectTerminalTotes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before connecting terminal totes.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/bongyuri.unity") throw new InvalidOperationException("Open bongyuri before connecting terminal totes.");
        var transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var pairs = new System.Collections.Generic.List<(string terminal, BoxAttacher loader, ToteBase tote)>();
        // Validate all three pairs before changing any scene references.
        foreach (string terminal in new[] { "A", "B", "C" })
        {
            var group = transforms.Single(t => t.name == terminal + "PartRobot");
            var loader = group.GetComponentsInChildren<BoxAttacher>(true).Single();
            var tote = group.GetComponentsInChildren<ToteBase>(true).Single();
            if (loader.name != "fr5_suction" + terminal || tote.name != "ParcelContainer_2x2x2_8" + terminal)
                throw new InvalidOperationException(terminal + " robot/tote names do not match the terminal group.");
            if (loader.tcp == null || loader.suctionCup == null || loader.armIK == null)
                throw new InvalidOperationException(terminal + " robot requires TCP, suction cup and BackArmIK.");
            if (tote.slots == null || tote.slots.Length != tote.Capacity || tote.slots.Any(s => s == null || !s.IsChildOf(tote.transform)) || tote.slots.Distinct().Count() != tote.Capacity)
                throw new InvalidOperationException(terminal + " tote requires eight unique child slots.");
            pairs.Add((terminal, loader, tote));
        }
        string folder = "Temp/BonguriIntegrationBackup";
        Directory.CreateDirectory(folder);
        string backup = folder + "/before_tote_connections_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity";
        if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Scene backup failed.");
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Connect terminal totes A B C");
        var report = new StringBuilder("Terminal tote connections\nBackup: " + backup + "\n");
        foreach (var pair in pairs)
        {
            Undo.RecordObjects(new UnityEngine.Object[] { pair.loader, pair.tote }, "Connect terminal tote");
            pair.loader.toto = pair.tote;
            pair.loader.container = pair.tote.transform;
            pair.loader.slots = (Transform[])pair.tote.slots.Clone();
            pair.tote.loader = pair.loader;
            EditorUtility.SetDirty(pair.loader); EditorUtility.SetDirty(pair.tote);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pair.loader);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pair.tote);
            if (pair.loader.toto != pair.tote || pair.tote.loader != pair.loader || pair.loader.container != pair.tote.transform || !pair.loader.slots.SequenceEqual(pair.tote.slots))
                throw new InvalidOperationException(pair.terminal + " connection verification failed.");
            report.AppendLine(pair.terminal + ": " + pair.loader.name + " <-> " + pair.tote.name + ", slots=" + pair.loader.slots.Length + ", robotActive=" + pair.loader.gameObject.activeInHierarchy + ", toteActive=" + pair.tote.gameObject.activeInHierarchy);
            foreach (var slot in pair.loader.slots) report.AppendLine("  " + slot.name + " " + slot.position.ToString("F4"));
        }
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
        File.WriteAllText("Temp/BonguriTerminalTotes.txt", report.ToString());
        Debug.Log("[Integration] Connected and verified all three terminal totes (8 slots each).");
    }
    [MenuItem("Tools/Bonguri/Integration/Connect Terminal Loading To Cell")]
    public static void ConnectTerminalLoading()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before connecting terminal loading.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/bongyuri.unity") throw new InvalidOperationException("Open bongyuri first.");
        var components = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
        var cell = components.OfType<CellController>().Single();
        var pairs = new[] { Destination.A, Destination.B, Destination.C }.Select(d => new {
            destination=d,
            line=components.OfType<ConveyorLine>().Single(l=>l.name=="Terminal"+d),
            loader=components.OfType<BoxAttacher>().Single(l=>l.name=="fr5_suction"+d)
        }).ToArray();
        foreach(var p in pairs) if(p.line.StopPoint==null || p.loader.toto==null || p.loader.toto.loader!=p.loader)throw new InvalidOperationException(p.destination+" terminal/tote is incomplete.");
        Directory.CreateDirectory("Temp/BonguriIntegrationBackup");
        string backup="Temp/BonguriIntegrationBackup/before_loading_handoff_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity";
        if(!EditorSceneManager.SaveScene(scene,backup,true))throw new IOException("Scene backup failed.");
        Undo.RecordObject(cell,"Connect terminal loading to CellController");
        var edit=new SerializedObject(cell);var array=edit.FindProperty("terminalStations");array.arraySize=3;
        var report=new StringBuilder("CellController terminal loading connections\n");
        for(int i=0;i<pairs.Length;i++)
        {
            var item=array.GetArrayElementAtIndex(i);var pair=pairs[i];
            item.FindPropertyRelative("destination").enumValueIndex=(int)pair.destination;
            item.FindPropertyRelative("line").objectReferenceValue=pair.line;
            item.FindPropertyRelative("loader").objectReferenceValue=pair.loader;
            item.FindPropertyRelative("maxPickupDistance").floatValue=1f;
            float distance=Vector3.Distance(pair.loader.transform.position,pair.line.StopPoint.position);
            report.AppendLine(pair.destination+": "+pair.line.name+" -> "+pair.loader.name+", stop-to-base="+distance.ToString("F3")+"m");
        }
        edit.ApplyModifiedProperties();
        if(!cell.ValidateTerminalConfiguration(out string error))throw new InvalidOperationException(error);
        EditorUtility.SetDirty(cell);EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed.");
        File.WriteAllText("Temp/BonguriTerminalLoading.txt",report.ToString());
        Debug.Log("[Integration] Connected A/B/C terminal loading to CellController. Confirm stop points are within robot reach.");
    }
    [MenuItem("Tools/Bonguri/Integration/Connect Bongyuri Process")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/bongyuri.unity" || scene.isDirty) throw new InvalidOperationException("Open and save bongyuri before setup.");
        if (GameObject.Find("BonguriProcess") != null) throw new InvalidOperationException("BonguriProcess already exists. Inspect existing connections.");
        Directory.CreateDirectory("Temp/BonguriIntegrationBackup");
        if (!EditorSceneManager.SaveScene(scene, "Temp/BonguriIntegrationBackup/bongyuri_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity", true)) throw new IOException("Scene backup failed.");
        var root = new GameObject("BonguriProcess");
        var cell = root.AddComponent<CellController>();
        var settings = AssetDatabase.LoadAssetAtPath<CellSettings>("Assets/_Factory/CellSettings.asset");
        var layout = AssetDatabase.LoadAssetAtPath<LabelLayout>("Assets/_Factory/LabelLayout.asset");
        Set(cell, "settings", settings); Set(cell, "correctTimeLimit", 90f);
        const float y = .8613f; const float z = 2.192f;
        var correct = Line(root, "CorrectLine", new Vector3(2.85f, y, z), new Vector3(1.1f, y, z), new Vector3(1.854f, y, z), cell);
        var read = Line(root, "ReadLine", new Vector3(1.1f, y, z), new Vector3(.25f, y, z), new Vector3(.7f, y, z), cell);
        var sort = Line(root, "SortLine", new Vector3(.25f, y, z), new Vector3(-2.65f, y, z), null, cell);
        // End checkpoints keep boxes present until terminal robot commissioning is complete.
        var a = Line(root, "TerminalA", new Vector3(-1.6441f, y, .7f), new Vector3(-1.6441f, y, -.5f), new Vector3(-1.6441f, y, -.3f), cell);
        var b = Line(root, "TerminalB", new Vector3(-2.65f, y, z), new Vector3(-4.2f, y, 2.05f), new Vector3(-4f, y, 2.068f), cell);
        var c = Line(root, "TerminalC", new Vector3(-1.6441f, y, 3.55f), new Vector3(-1.6441f, y, 4.9f), new Vector3(-1.6441f, y, 4.7f), cell);
        var ng = Line(root, "NGCheckpoint", new Vector3(-.3741f, y, .9f), new Vector3(-.3741f, y, .5f), new Vector3(-.3741f, y, .65f), cell);
        Set(correct, "nextLine", read); Set(read, "nextLine", sort); Set(sort, "nextLine", b);
        var pushers = UnityEngine.Object.FindObjectsByType<BoxPusher>(FindObjectsSortMode.None);
        var pa = pushers.Single(p => p.name == "SeparatorA"); var pc = pushers.Single(p => p.name == "SeparatorC"); var pn = pushers.Single(p => p.name == "SeparatorNG");
        ConfigurePusher(pa, Destination.A, a, new Vector3(-1.6441f, y, z), root);
        ConfigurePusher(pc, Destination.C, c, new Vector3(-1.6441f, y, z), root);
        ConfigurePusher(pn, Destination.NG, ng, new Vector3(-.3741f, y, z), root);
        var sortEdit = new SerializedObject(sort); var array = sortEdit.FindProperty("pushers"); array.arraySize = 3;
        array.GetArrayElementAtIndex(0).objectReferenceValue = pn; array.GetArrayElementAtIndex(1).objectReferenceValue = pa; array.GetArrayElementAtIndex(2).objectReferenceValue = pc; sortEdit.ApplyModifiedPropertiesWithoutUndo();
        var rig = GameObject.Find("ScanStation").GetComponent<FaceCameraRig>();
        Set(rig, "stopPoint", Ref(correct, "stopPoint")); Set(rig, "stopPointIsBoxCenter", false);
        Set(rig, "boxSize", new Vector3(.07f, .06f, .07f)); Set(rig, "cameraDistance", .3f);
        var rigEdit = new SerializedObject(rig); rigEdit.FindProperty("cullingMask").intValue = 1 << LayerMask.NameToLayer("ScanTarget"); rigEdit.ApplyModifiedPropertiesWithoutUndo(); rig.BuildRig();
        var detector = rig.GetComponent<LabelPresenceDetector>() ?? rig.gameObject.AddComponent<LabelPresenceDetector>();
        Set(detector, "model", AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>("Assets/FactoryMLModels/LabelPresence.onnx"));
        var readerGO = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Process/LabelReaderStation.prefab")); readerGO.transform.SetParent(root.transform);
        var reader = readerGO.GetComponent<LabelReader>(); Set(reader, "settings", settings); Set(reader, "layout", layout);
        var feeder = new GameObject("Feeder").AddComponent<BoxFeeder>(); feeder.transform.SetParent(root.transform);
        Set(feeder, "cell", cell); Set(feeder, "line", correct); Set(feeder, "layout", layout);
        Set(feeder, "boxPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Process/ParcelBox_6x8x12cm_Process.prefab").GetComponent<BoxInfo>());
        var correction = UnityEngine.Object.FindObjectsByType<CorrectionSequence>(FindObjectsSortMode.None).Single(r => r.name.Contains("first"));
        Physics.SyncTransforms();
        var surface = Physics.RaycastAll(new Vector3(1.854f, 1.4f, z), Vector3.down, 1).Where(h => h.collider.name.Contains("Conveyor") || h.collider.name.Contains("Surface")).OrderByDescending(h => h.point.y).FirstOrDefault();
        correction.placementSurface = surface.collider;
        Set(cell, "correctLine", correct); Set(cell, "readLine", read); Set(cell, "feeder", feeder); Set(cell, "faceRig", rig); Set(cell, "faceDetector", detector); Set(cell, "reader", reader); Set(cell, "placeholderRead", false); Set(cell, "correction", correction);
        // Retain manually placed test props in the scene, disabled during process commissioning.
        foreach (var go in scene.GetRootGameObjects()) if (go.name.StartsWith("ParcelBox") || go.name == "RejectTestParcel" || go.name == "PusherTestParcel" || go.name == "dataSet" || go.name == "IK_TestPedestal") go.SetActive(false);
        var generator = rig.GetComponent<LabelDatasetGenerator>(); if (generator != null) generator.enabled = false;
        foreach (var preview in UnityEngine.Object.FindObjectsByType<FacePreviewPanel>(FindObjectsSortMode.None)) preview.enabled = false;
        var canvas = rig.transform.Find("FacePreviewCanvas"); if (canvas != null) canvas.gameObject.SetActive(false);
        foreach (var conveyor in UnityEngine.Object.FindObjectsByType<RoboLacyTech.Conveyors.LinearConveyor>(FindObjectsSortMode.None)) conveyor.enabled = false;
        var panel = root.AddComponent<BonguriIntegrationPanel>(); panel.cell = cell; panel.feeder = feeder; panel.terminalLines = new[] { a, b, c, ng };
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/BonguriIntegration.setup.txt", "Connected Cell/Feeder/ONNX/OCR/Correction/Pushers; terminal checkpoint A/B/C/NG. Final terminal loading is not wired. Backup in Temp/BonguriIntegrationBackup.");
        Selection.activeGameObject = root;
    }
    static UnityEngine.Object Ref(UnityEngine.Object target, string name) => new SerializedObject(target).FindProperty(name).objectReferenceValue;
    static void Set(UnityEngine.Object target, string name, object value)
    {
        var so = new SerializedObject(target); var p = so.FindProperty(name); if (p == null) throw new Exception(target.name + " missing " + name);
        if (value is bool flag) p.boolValue = flag; else if (value is float f) p.floatValue = f; else if (value is Vector3 v) p.vector3Value = v; else if (value is int i) p.intValue = i; else p.objectReferenceValue = (UnityEngine.Object)value;
        so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(target);
    }
    static Transform Point(GameObject root, string name, Vector3 p)
    { var t = new GameObject(name).transform; t.SetParent(root.transform); t.position = p; return t; }
    static ConveyorLine Line(GameObject root, string name, Vector3 entry, Vector3 exit, Vector3? stop, CellController cell)
    {
        var go = new GameObject(name); go.transform.SetParent(root.transform); var line = go.AddComponent<ConveyorLine>();
        Set(line, "entry", Point(go, "Entry", entry)); Set(line, "exit", Point(go, "Exit", exit)); Set(line, "cell", cell); Set(line, "beltWidth", .8f);
        if (stop.HasValue) Set(line, "stopPoint", Point(go, "Stop", stop.Value));
        var so = new SerializedObject(line); so.FindProperty("lineName").stringValue = name; so.ApplyModifiedPropertiesWithoutUndo(); return line;
    }
    static void ConfigurePusher(BoxPusher p, Destination destination, ConveyorLine to, Vector3 divert, GameObject root)
    {
        Set(p, "target", (int)destination); Set(p, "divertPoint", Point(root, p.name + "_Divert", divert)); Set(p, "pusherHead", p.transform); Set(p, "toLine", to);
        // 이동 거리는 BoxPusher Inspector 설정을 유지한다.
    }
    [MenuItem("Tools/Bonguri/Integration/Audit Bongyuri Scene")]
    public static void Audit()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the current scene before integration.");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");
        var text = new StringBuilder();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string path = t.name; for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                var components = t.GetComponents<MonoBehaviour>().Where(c => c != null).ToArray();
                if (components.Length == 0 && !new[] { "conveyor", "belt", "stop", "robot", "fr5", "toto", "tote", "pusher", "separator" }.Any(w => t.name.ToLowerInvariant().Contains(w))) continue;
                text.AppendLine(path + " pos=" + t.position.ToString("F4") + " scale=" + t.lossyScale.ToString("F3") + " active=" + t.gameObject.activeInHierarchy);
                foreach (var c in components)
                {
                    text.AppendLine("  " + c.GetType().Name);
                    var so = new SerializedObject(c); var it = so.GetIterator();
                    while (it.NextVisible(true))
                    {
                        if (it.propertyType == SerializedPropertyType.ObjectReference) text.AppendLine("    " + it.propertyPath + "=" + (it.objectReferenceValue == null ? "null" : it.objectReferenceValue.name));
                    }
                }
            }
        File.WriteAllText("Temp/BonguriIntegration.audit.txt", text.ToString());
        var geometry = new StringBuilder();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("ConveyorBeltStraightMixedLOD0") || t.name.StartsWith("ParcelBox") || t.name.StartsWith("Separator"))
                {
                    var r = t.GetComponent<Renderer>(); var c = t.GetComponent<Collider>();
                    geometry.AppendLine(t.name + " p=" + t.position.ToString("F4") + " rot=" + t.eulerAngles + " bounds=" + (c != null ? c.bounds.ToString("F4") : r != null ? r.bounds.ToString("F4") : "none"));
                }
            }
        File.WriteAllText("Temp/BonguriIntegration.geometry.txt", geometry.ToString());
        Debug.Log("[Integration] Scene audit saved.");
    }
}
