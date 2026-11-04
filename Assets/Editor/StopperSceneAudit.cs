using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class StopperSceneAudit
{
    public static void Run()
    {
        try{
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");
            var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var lines=all.Select(t=>t.GetComponent<ConveyorLine>()).Where(x=>x!=null).ToArray();
            var report=new List<string>();
            var named=all.Where(t=>t.name.IndexOf("stop",StringComparison.OrdinalIgnoreCase)>=0||t.name.Contains("스토퍼")).ToArray();
            foreach(var t in named)report.Add("NAME CANDIDATE "+Path(t)+" position="+t.position+" renderers="+t.GetComponentsInChildren<Renderer>(true).Length+" colliders="+t.GetComponentsInChildren<Collider>(true).Length);
            foreach(var line in lines){
                var so=new SerializedObject(line);var entry=so.FindProperty("entry").objectReferenceValue as Transform;var exit=so.FindProperty("exit").objectReferenceValue as Transform;var stop=so.FindProperty("stopPoint").objectReferenceValue as Transform;
                so.FindProperty("stopperDrop").floatValue=.05f;so.FindProperty("stopperMoveTime").floatValue=.2f;
                var existing=so.FindProperty("stopper").objectReferenceValue as Transform;
                // Only a physical, uniquely named stopper nested under this logical line
                // is eligible for automatic binding. Stop markers and UI buttons never qualify.
                var candidates=line.GetComponentsInChildren<Transform>(true).Where(t=>t!=line.transform&&(t.name.IndexOf("stopper",StringComparison.OrdinalIgnoreCase)>=0||t.name.Contains("스토퍼"))&&(t.GetComponentInChildren<Renderer>(true)!=null||t.GetComponentInChildren<Collider>(true)!=null)).ToArray();
                if(existing==null&&candidates.Length==1){existing=candidates[0];so.FindProperty("stopper").objectReferenceValue=existing;}
                so.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(line);
                report.Add("\nLINE "+Path(line.transform)+" entry="+(entry==null?"null":entry.position.ToString("F4"))+" exit="+(exit==null?"null":exit.position.ToString("F4")));
                if(stop==null)report.Add("STOPPOINT none (pass-through line)");
                else if(entry==null||exit==null)report.Add("STOPPOINT cannot validate: missing entry/exit");
                else if(so.FindProperty("useSurfacePath").boolValue){
                    if(!line.ValidateSurfacePath(out string error))throw new Exception(line.name+": "+error);
                    var points=new List<Vector3>{entry.position};var way=so.FindProperty("surfaceWaypoints");for(int i=0;i<way.arraySize;i++){var t=way.GetArrayElementAtIndex(i).objectReferenceValue as Transform;if(t!=null)points.Add(t.position);}points.Add(exit.position);
                    float nearest=float.PositiveInfinity;foreach(var pair in points.Zip(points.Skip(1),(a,b)=>(a,b))){var delta=pair.b-pair.a;var p=pair.a+delta*Mathf.Clamp01(Vector3.Dot(stop.position-pair.a,delta)/Mathf.Max(delta.sqrMagnitude,.000001f));nearest=Mathf.Min(nearest,Vector3.Distance(stop.position,p));}
                    report.Add("STOPPOINT "+Path(stop)+" "+stop.position.ToString("F4")+" distance to surface path="+nearest.ToString("F4")+"m; path "+(nearest<=.05f?"OK":"CHECK"));
                }else{
                    var delta=exit.position-entry.position;var flat=Vector3.ProjectOnPlane(delta,Vector3.up);var relative=Vector3.ProjectOnPlane(stop.position-entry.position,Vector3.up);float along=Vector3.Dot(relative,flat.normalized);float lateral=(relative-flat.normalized*along).magnitude;
                    report.Add("STOPPOINT "+Path(stop)+" "+stop.position.ToString("F4")+" along="+along.ToString("F3")+"/"+flat.magnitude.ToString("F3")+"m lateral="+lateral.ToString("F4")+"m heightDelta="+(stop.position.y-entry.position.y).ToString("F4")+"m "+(along>=0&&along<=flat.magnitude+.001f&&lateral<.05f?"OK":"CHECK"));
                }
                if(existing==null){report.Add("STOPPER none uniquely identified; not created or assigned; drop=.05m move=.2s");continue;}
                report.Add("STOPPER assigned="+Path(existing)+" raised transform="+existing.position);
                if(stop!=null){var colliders=existing.GetComponentsInChildren<Collider>(true);if(colliders.Length==0)report.Add("STOPPER collision geometry missing; clearance unverified");foreach(var c in colliders)report.Add("STOPPER top raised="+c.bounds.max.y+" lowered="+(c.bounds.max.y-.05f)+" belt reference="+stop.position.y+" clears="+(c.bounds.max.y-.05f<stop.position.y));}
            }
            // Geometry candidates near station points are reported only, never selected by proximity.
            foreach(var line in lines){var stop=new SerializedObject(line).FindProperty("stopPoint").objectReferenceValue as Transform;if(stop==null)continue;foreach(var c in all.Select(t=>t.GetComponent<BoxCollider>()).Where(c=>c!=null&&Mathf.Abs(c.bounds.center.y-stop.position.y)<.15f&&Vector3.Distance(c.bounds.center,stop.position)<.35f))report.Add("NEARBY UNASSIGNED "+line.name+": "+Path(c.transform)+" bounds="+c.bounds);}
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllLines("Logs/StopperSceneAudit.txt",report);EditorApplication.Exit(0);
        }catch(Exception e){Debug.LogException(e);File.WriteAllText("Logs/StopperSceneAudit.txt","FAIL "+e);EditorApplication.Exit(1);}
    }
    static string Path(Transform t){return t.parent==null?t.name:Path(t.parent)+"/"+t.name;}
}
