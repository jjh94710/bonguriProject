using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BonguriConveyorSurfaceSetup
{
    static Collider[] Surfaces() => SceneManager.GetActiveScene().GetRootGameObjects()
        .SelectMany(r=>r.GetComponentsInChildren<Collider>(true))
        .Where(c=>c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger && c.name.Contains("Conveyor") && !c.name.Contains("Leg")).ToArray();
    public static void Audit()
    {
        var text=new StringBuilder();
        foreach(var c in Surfaces())
        {
            text.AppendLine(c.name+" "+c.GetType().Name+" | p="+c.transform.position.ToString("F4")+" | center="+c.bounds.center.ToString("F4")+" | size="+c.bounds.size.ToString("F4"));
        }
        File.WriteAllText("Temp/ConveyorSurfaceGeometry.txt",text.ToString());
    }

    sealed class RoutePlan { public string terminal;public ConveyorLine line;public Collider[] surfaces;public Vector3[] points; }
    static Vector3 End(Collider collider, bool positive)
    {
        var box=(BoxCollider)collider;
        Vector3 p=collider.transform.TransformPoint(box.center+Vector3.forward*(positive?1:-1)*box.size.z*.5f);
        p.y=collider.bounds.max.y;return p;
    }
    static void Span(System.Collections.Generic.List<Vector3> points,Vector3 to)
    {
        if(points.Count==0){points.Add(to);return;}
        Vector3 from=points[points.Count-1];int n=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/.2f));
        for(int i=1;i<=n;i++)if(Vector3.Distance(points[points.Count-1],Vector3.Lerp(from,to,(float)i/n))>.001f)points.Add(Vector3.Lerp(from,to,(float)i/n));
    }
    static RoutePlan Plan(string terminal,Collider[] all)
    {
        Func<string,Collider> find=name=>all.Single(c=>c.name==name);
        var line=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None).Single(l=>l.name=="Terminal"+terminal);
        var pusher=UnityEngine.Object.FindObjectsByType<BoxPusher>(FindObjectsSortMode.None).FirstOrDefault(p=>p.name=="Separator"+terminal);
        string[] names=terminal=="A"?new[]{"ConveyorBeltStraightMixed.029 (2)","Conveyor90DegreeTurnMixed.005","ConveyorBeltStraightMixed.029 (1)","ConveyorBeltInclinedMixed.003","ConveyorBeltStraightMixed.021","ConveyorBeltInclinedMixed.004","ConveyorBeltStraightMixed.024","ConveyorBeltStraightMixed.029"}:
            terminal=="C"?new[]{"ConveyorBeltStraightMixed.029 (3)","Conveyor90DegreeTurnMixed.005 (1)","ConveyorBeltStraightMixed.027 (1)","ConveyorBeltInclinedMixed.001","ConveyorBeltStraightMixed.019","ConveyorBeltInclinedMixed.006","ConveyorBeltStraightMixed.026","ConveyorBeltStraightMixed.027"}:
            new[]{"ConveyorBeltStraightMixed.028 (1)","ConveyorBeltInclinedMixed.002","ConveyorBeltStraightMixed.020","ConveyorBeltInclinedMixed.005","ConveyorBeltStraightMixed.025","ConveyorBeltStraightMixed.028"};
        var surfaces=names.Select(find).ToArray();
        var raw=new System.Collections.Generic.List<Vector3>();
        var edit=new SerializedObject(line);var entry=(Transform)edit.FindProperty("entry").objectReferenceValue;
        Vector3 start=entry.position;
        if(pusher!=null)
        {
            var ps=new SerializedObject(pusher);float push=ps.FindProperty("pushDistance").floatValue;
            start=pusher.DivertPoint.position+pusher.transform.forward*push;
        }
        Span(raw,start);
        int firstStraight=0;
        if(terminal!="B")
        {
            Vector3 e0=End(surfaces[0],false),e1=End(surfaces[0],true);
            Vector3 turnStart=terminal=="C"?(e0.z>e1.z?e0:e1):(e0.z<e1.z?e0:e1),turnEnd=End(surfaces[2],true);
            Span(raw,turnStart);
            Vector3 incoming=(turnStart-start).normalized;incoming.y=0;incoming.Normalize();
            Vector3 outgoing=-surfaces[2].transform.forward;outgoing.y=0;outgoing.Normalize();
            float handle=Vector3.Distance(turnStart,turnEnd)*.39f;
            Vector3 c1=turnStart+incoming*handle,c2=turnEnd-outgoing*handle;
            for(int i=1;i<=12;i++)
            {float t=i/12f;raw.Add(Mathf.Pow(1-t,3)*turnStart+3*Mathf.Pow(1-t,2)*t*c1+3*(1-t)*t*t*c2+t*t*t*turnEnd);}
            firstStraight=2;
        }
        for(int i=firstStraight;i<surfaces.Length;i++)
        {
            if(!(surfaces[i] is BoxCollider))continue;
            Span(raw,End(surfaces[i],true));
            Vector3 end=End(surfaces[i],false);
            if(i==surfaces.Length-1)end+=surfaces[i].transform.forward*.18f;
            Span(raw,end);
        }
        var projector=new ConveyorSurfacePath(raw.ToArray(),surfaces,.002f,.18f,35);
        var points=new System.Collections.Generic.List<Vector3>();
        foreach(var expected in raw)
        {
            if(!projector.TrySurface(expected,out var surface,out _))throw new InvalidOperationException(terminal+" no belt surface at "+expected.ToString("F4"));
            if(points.Count==0||Vector3.Distance(points[points.Count-1],surface)>.001f)points.Add(surface);
        }
        var path=new ConveyorSurfacePath(points.ToArray(),surfaces,.002f,.18f,35);
        if(!path.Validate(out string error))throw new InvalidOperationException(terminal+" path: "+error);
        return new RoutePlan{terminal=terminal,line=line,surfaces=surfaces,points=points.ToArray()};
    }
    [MenuItem("Tools/Bonguri/Integration/Connect Belt Surface Routes A B C")]
    public static void Connect()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play first.");
        var scene=SceneManager.GetActiveScene();if(scene.path!="Assets/Scenes/bongyuri.unity")throw new InvalidOperationException("Open bongyuri first.");
        Physics.SyncTransforms();var surfaces=Surfaces();var plans=new[]{Plan("A",surfaces),Plan("B",surfaces),Plan("C",surfaces)};
        foreach(var plan in plans)
            if(plan.line.transform.Find("SurfaceRoute")!=null)throw new InvalidOperationException("SurfaceRoute already exists; edit its waypoints instead of rerunning setup.");
        var probe=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/Process/ParcelBox_6x8x12cm_Process.prefab"));
        try
        {
            var box=probe.GetComponent<BoxInfo>();probe.transform.position=new Vector3(100,100,100);var body=probe.GetComponent<Rigidbody>();body.isKinematic=true;
            foreach(var plan in plans)
            {
                var path=new ConveyorSurfacePath(plan.points,plan.surfaces,.002f,.18f,35);
                for(float d=0;d<=path.Length;d+=.02f)
                    if(!path.TryPose(box,Quaternion.identity,d,0,out _,out _,out string error))throw new InvalidOperationException(plan.terminal+" parcel at "+d.ToString("F2")+"m: "+error);
            }
        }
        finally{UnityEngine.Object.DestroyImmediate(probe);}
        Directory.CreateDirectory("Temp/BonguriIntegrationBackup");
        if(!EditorSceneManager.SaveScene(scene,"Temp/BonguriIntegrationBackup/before_surface_routes_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".unity",true))throw new IOException("Backup failed.");
        Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Connect belt surface routes");
        var report=new StringBuilder();
        foreach(var plan in plans)
        {
            var edit=new SerializedObject(plan.line);var entry=(Transform)edit.FindProperty("entry").objectReferenceValue;var exit=(Transform)edit.FindProperty("exit").objectReferenceValue;
            var stop=plan.line.StopPoint;
            Undo.RecordObjects(new UnityEngine.Object[]{plan.line,entry,exit,stop},"Connect surface route");
            entry.position=plan.points[0];exit.position=plan.points[plan.points.Length-1];stop.position=exit.position;
            var root=new GameObject("SurfaceRoute");root.transform.SetParent(plan.line.transform,false);Undo.RegisterCreatedObjectUndo(root,"Create belt surface waypoints");
            edit.FindProperty("useSurfacePath").boolValue=true;edit.FindProperty("surfaceProbeHeight").floatValue=.18f;
            var points=edit.FindProperty("surfaceWaypoints");points.arraySize=plan.points.Length-2;
            for(int i=1;i<plan.points.Length-1;i++){var t=new GameObject("Surface_"+i.ToString("D3")).transform;t.SetParent(root.transform,false);t.position=plan.points[i];points.GetArrayElementAtIndex(i-1).objectReferenceValue=t;}
            var colliders=edit.FindProperty("beltSurfaces");colliders.arraySize=plan.surfaces.Length;
            for(int i=0;i<plan.surfaces.Length;i++)colliders.GetArrayElementAtIndex(i).objectReferenceValue=plan.surfaces[i];
            edit.ApplyModifiedProperties();EditorUtility.SetDirty(plan.line);
            if(!plan.line.ValidateSurfacePath(out string error))throw new InvalidOperationException(error);
            report.AppendLine(plan.terminal+": "+plan.points.Length+" surface points, "+plan.surfaces.Length+" belt colliders, "+plan.line.PathLength.ToString("F3")+"m");
        }
        Undo.CollapseUndoOperations(group);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/ConveyorSurfaceRoutes.txt",report.ToString());Debug.Log("[Surface Routes] "+report);
    }
}
