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
    static BonguriIntegrationSetup() { EditorApplication.update += Poll; if(!string.IsNullOrEmpty(SessionState.GetString("BonguriIntegration.Run","")))EditorApplication.update+=StartRequestedRun; }
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
            if (command == "run-top" || command == "run-side")
            {
                SessionState.SetString("BonguriIntegration.Run", command);
                EditorApplication.isPlaying=true;
                EditorApplication.update+=StartRequestedRun;
            }
        }
        catch (Exception e) { File.WriteAllText("Temp/BonguriIntegration.error.txt", e.ToString()); Debug.LogException(e); }
    }
    static void StartRequestedRun()
    {
        if(!EditorApplication.isPlaying || Time.time<1) return;
        EditorApplication.update-=StartRequestedRun;
        var panel=UnityEngine.Object.FindFirstObjectByType<BonguriIntegrationPanel>();
        if(panel!=null)panel.RunSmoke(SessionState.GetString("BonguriIntegration.Run","")=="run-side");
        SessionState.EraseString("BonguriIntegration.Run");
    }
    [MenuItem("Tools/Bonguri/Integration/Connect Bongyuri Process")]
    public static void Setup()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/bongyuri.unity" || scene.isDirty) throw new InvalidOperationException("Open and save bongyuri before setup.");
        if(GameObject.Find("BonguriProcess")!=null)throw new InvalidOperationException("BonguriProcess already exists. Inspect existing connections.");
        Directory.CreateDirectory("Temp/BonguriIntegrationBackup");
        if(!EditorSceneManager.SaveScene(scene,"Temp/BonguriIntegrationBackup/bongyuri_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity",true))throw new IOException("Scene backup failed.");
        var root=new GameObject("BonguriProcess");
        var cell=root.AddComponent<CellController>();
        var settings=AssetDatabase.LoadAssetAtPath<CellSettings>("Assets/_Factory/CellSettings.asset");
        var layout=AssetDatabase.LoadAssetAtPath<LabelLayout>("Assets/_Factory/LabelLayout.asset");
        Set(cell,"settings",settings);Set(cell,"correctTimeLimit",90f);
        const float y=.8613f;const float z=2.192f;
        var correct=Line(root,"CorrectLine",new Vector3(2.85f,y,z),new Vector3(1.1f,y,z),new Vector3(1.854f,y,z),cell);
        var read=Line(root,"ReadLine",new Vector3(1.1f,y,z),new Vector3(.25f,y,z),new Vector3(.7f,y,z),cell);
        var sort=Line(root,"SortLine",new Vector3(.25f,y,z),new Vector3(-2.65f,y,z),null,cell);
        // End checkpoints keep boxes present until terminal robot commissioning is complete.
        var a=Line(root,"TerminalA",new Vector3(-1.6441f,y,.7f),new Vector3(-1.6441f,y,-.5f),new Vector3(-1.6441f,y,-.3f),cell);
        var b=Line(root,"TerminalB",new Vector3(-2.65f,y,z),new Vector3(-4.2f,y,2.05f),new Vector3(-4f,y,2.068f),cell);
        var c=Line(root,"TerminalC",new Vector3(-1.6441f,y,3.55f),new Vector3(-1.6441f,y,4.9f),new Vector3(-1.6441f,y,4.7f),cell);
        var ng=Line(root,"NGCheckpoint",new Vector3(-.3741f,y,.9f),new Vector3(-.3741f,y,.5f),new Vector3(-.3741f,y,.65f),cell);
        Set(correct,"nextLine",read);Set(read,"nextLine",sort);Set(sort,"nextLine",b);
        var pushers=UnityEngine.Object.FindObjectsByType<BoxPusher>(FindObjectsSortMode.None);
        var pa=pushers.Single(p=>p.name=="SeparatorA");var pc=pushers.Single(p=>p.name=="SeparatorC");var pn=pushers.Single(p=>p.name=="SeparatorNG");
        ConfigurePusher(pa,Destination.A,a,new Vector3(-1.6441f,y,z),root);
        ConfigurePusher(pc,Destination.C,c,new Vector3(-1.6441f,y,z),root);
        ConfigurePusher(pn,Destination.NG,ng,new Vector3(-.3741f,y,z),root);
        var sortEdit=new SerializedObject(sort);var array=sortEdit.FindProperty("pushers");array.arraySize=3;
        array.GetArrayElementAtIndex(0).objectReferenceValue=pn;array.GetArrayElementAtIndex(1).objectReferenceValue=pa;array.GetArrayElementAtIndex(2).objectReferenceValue=pc;sortEdit.ApplyModifiedPropertiesWithoutUndo();
        var rig=GameObject.Find("ScanStation").GetComponent<FaceCameraRig>();
        Set(rig,"stopPoint",Ref(correct,"stopPoint"));Set(rig,"stopPointIsBoxCenter",false);
        Set(rig,"boxSize",new Vector3(.07f,.06f,.07f));Set(rig,"cameraDistance",.3f);
        var rigEdit=new SerializedObject(rig);rigEdit.FindProperty("cullingMask").intValue=1<<LayerMask.NameToLayer("ScanTarget");rigEdit.ApplyModifiedPropertiesWithoutUndo();rig.BuildRig();
        var detector=rig.GetComponent<LabelPresenceDetector>()??rig.gameObject.AddComponent<LabelPresenceDetector>();
        Set(detector,"model",AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>("Assets/FactoryMLModels/LabelPresence.onnx"));
        var readerGO=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Process/LabelReaderStation.prefab"));readerGO.transform.SetParent(root.transform);
        var reader=readerGO.GetComponent<LabelReader>();Set(reader,"settings",settings);Set(reader,"layout",layout);
        var feeder=new GameObject("Feeder").AddComponent<BoxFeeder>();feeder.transform.SetParent(root.transform);
        Set(feeder,"cell",cell);Set(feeder,"line",correct);Set(feeder,"layout",layout);
        Set(feeder,"boxPrefab",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Process/ParcelBox_6x8x12cm_Process.prefab").GetComponent<BoxInfo>());
        var correction=UnityEngine.Object.FindObjectsByType<CorrectionSequence>(FindObjectsSortMode.None).Single(r=>r.name.Contains("first"));
        Physics.SyncTransforms();
        var surface=Physics.RaycastAll(new Vector3(1.854f,1.4f,z),Vector3.down,1).Where(h=>h.collider.name.Contains("Conveyor")||h.collider.name.Contains("Surface")).OrderByDescending(h=>h.point.y).FirstOrDefault();
        correction.placementSurface=surface.collider;
        Set(cell,"correctLine",correct);Set(cell,"readLine",read);Set(cell,"feeder",feeder);Set(cell,"faceRig",rig);Set(cell,"faceDetector",detector);Set(cell,"reader",reader);Set(cell,"placeholderRead",false);Set(cell,"correction",correction);
        // Retain manually placed test props in the scene, disabled during process commissioning.
        foreach(var go in scene.GetRootGameObjects()) if(go.name.StartsWith("ParcelBox")||go.name=="RejectTestParcel"||go.name=="PusherTestParcel"||go.name=="dataSet"||go.name=="IK_TestPedestal")go.SetActive(false);
        var generator=rig.GetComponent<LabelDatasetGenerator>();if(generator!=null)generator.enabled=false;
        foreach(var preview in UnityEngine.Object.FindObjectsByType<FacePreviewPanel>(FindObjectsSortMode.None))preview.enabled=false;
        var canvas=rig.transform.Find("FacePreviewCanvas");if(canvas!=null)canvas.gameObject.SetActive(false);
        foreach(var conveyor in UnityEngine.Object.FindObjectsByType<RoboLacyTech.Conveyors.LinearConveyor>(FindObjectsSortMode.None))conveyor.enabled=false;
        var panel=root.AddComponent<BonguriIntegrationPanel>();panel.cell=cell;panel.feeder=feeder;panel.terminalLines=new[]{a,b,c,ng};
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/BonguriIntegration.setup.txt","Connected Cell/Feeder/ONNX/OCR/Correction/Pushers; terminal checkpoint A/B/C/NG. Final terminal loading is not wired. Backup in Temp/BonguriIntegrationBackup.");
        Selection.activeGameObject=root;
    }
    static UnityEngine.Object Ref(UnityEngine.Object target,string name)=>new SerializedObject(target).FindProperty(name).objectReferenceValue;
    static void Set(UnityEngine.Object target,string name,object value)
    {
        var so=new SerializedObject(target);var p=so.FindProperty(name);if(p==null)throw new Exception(target.name+" missing "+name);
        if(value is bool flag)p.boolValue=flag;else if(value is float f)p.floatValue=f;else if(value is Vector3 v)p.vector3Value=v;else if(value is int i)p.intValue=i;else p.objectReferenceValue=(UnityEngine.Object)value;
        so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(target);
    }
    static Transform Point(GameObject root,string name,Vector3 p)
    { var t=new GameObject(name).transform;t.SetParent(root.transform);t.position=p;return t; }
    static ConveyorLine Line(GameObject root,string name,Vector3 entry,Vector3 exit,Vector3? stop,CellController cell)
    {
        var go=new GameObject(name);go.transform.SetParent(root.transform);var line=go.AddComponent<ConveyorLine>();
        Set(line,"entry",Point(go,"Entry",entry));Set(line,"exit",Point(go,"Exit",exit));Set(line,"cell",cell);Set(line,"beltWidth",.8f);
        if(stop.HasValue)Set(line,"stopPoint",Point(go,"Stop",stop.Value));
        var so=new SerializedObject(line);so.FindProperty("lineName").stringValue=name;so.ApplyModifiedPropertiesWithoutUndo();return line;
    }
    static void ConfigurePusher(BoxPusher p,Destination destination,ConveyorLine to,Vector3 divert,GameObject root)
    {
        Set(p,"target",(int)destination);Set(p,"divertPoint",Point(root,p.name+"_Divert",divert));Set(p,"pusherHead",p.transform);Set(p,"toLine",to);
        var entry=(Transform)Ref(to,"entry");Set(p,"pushDistance",Mathf.Abs(entry.position.z-divert.z));
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
            if (components.Length == 0 && !new[]{"conveyor","belt","stop","robot","fr5","toto","tote","pusher","separator"}.Any(w=>t.name.ToLowerInvariant().Contains(w))) continue;
            text.AppendLine(path + " pos=" + t.position.ToString("F4") + " scale=" + t.lossyScale.ToString("F3") + " active=" + t.gameObject.activeInHierarchy);
            foreach (var c in components)
            {
                text.AppendLine("  " + c.GetType().Name);
                var so = new SerializedObject(c); var it = so.GetIterator();
                while(it.NextVisible(true))
                {
                    if(it.propertyType == SerializedPropertyType.ObjectReference) text.AppendLine("    " + it.propertyPath + "=" + (it.objectReferenceValue == null ? "null" : it.objectReferenceValue.name));
                }
            }
        }
        File.WriteAllText("Temp/BonguriIntegration.audit.txt", text.ToString());
        var geometry = new StringBuilder();
        foreach(var root in scene.GetRootGameObjects())
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("ConveyorBeltStraightMixedLOD0") || t.name.StartsWith("ParcelBox") || t.name.StartsWith("Separator"))
            {
                var r=t.GetComponent<Renderer>();var c=t.GetComponent<Collider>();
                geometry.AppendLine(t.name+" p="+t.position.ToString("F4")+" rot="+t.eulerAngles+" bounds="+(c!=null?c.bounds.ToString("F4"):r!=null?r.bounds.ToString("F4"):"none"));
            }
        }
        File.WriteAllText("Temp/BonguriIntegration.geometry.txt",geometry.ToString());
        Debug.Log("[Integration] Scene audit saved.");
    }
}
