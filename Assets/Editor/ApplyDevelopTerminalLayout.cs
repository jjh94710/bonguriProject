using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class ApplyDevelopTerminalLayout {
 static List<string> report=new List<string>();
 static T[] All<T>(Scene s) where T:Component=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
 static void CopyPose(Transform dst,Transform src){dst.SetPositionAndRotation(src.position,src.rotation);var p=dst.parent==null?Vector3.one:dst.parent.lossyScale;dst.localScale=new Vector3(src.lossyScale.x/p.x,src.lossyScale.y/p.y,src.lossyScale.z/p.z);EditorUtility.SetDirty(dst);PrefabUtility.RecordPrefabInstancePropertyModifications(dst);}
 static Dictionary<UnityEngine.Object,string> UI(Scene s){var transforms=All<Canvas>(s).SelectMany(c=>c.GetComponentsInChildren<Transform>(true)).Concat(All<Camera>(s).Select(c=>c.transform)).Distinct().ToArray();return transforms.SelectMany(t=>t.GetComponents<Component>()).Where(c=>c!=null).Distinct().ToDictionary(c=>(UnityEngine.Object)c,c=>EditorJsonUtility.ToJson(c));}
 static Transform Pedestal(Scene scene,Transform owner,Transform[] excluded){var candidates=All<BoxCollider>(scene).Where(c=>!excluded.Any(e=>c.transform==e||c.transform.IsChildOf(e))&&c.bounds.size.y>.2f&&c.bounds.size.x<.5f&&c.bounds.size.z<.5f&&Mathf.Abs(c.bounds.max.y-owner.position.y)<.15f&&Vector2.Distance(new Vector2(c.bounds.center.x,c.bounds.center.z),new Vector2(owner.position.x,owner.position.z))<.13f).ToArray();return candidates.Length==1?candidates[0].transform:null;}
 public static void Run(){try{
 var current=EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");var preserved=UI(current);var source=EditorSceneManager.OpenScene("Assets/LayoutComparisonTemp/DevelopLayout.unity",OpenSceneMode.Additive);
 var dst=All<LoadSequence>(current);var src=All<LoadSequence>(source);if(dst.Length!=3||src.Length!=3)throw new Exception("Expected 3 terminal loaders per scene");
 var dstExclude=dst.SelectMany(s=>new[]{s.Attacher.transform,s.Attacher.tote.transform}).ToArray();var srcExclude=src.SelectMany(s=>new[]{s.Attacher.transform,s.Attacher.tote.transform}).ToArray();
 foreach(var d in dst){var s=src.Single(x=>x.Terminal==d.Terminal);var a=d.Attacher;var b=s.Attacher;
 foreach(var pair in new[]{(a.transform,b.transform),(a.tote.transform,b.tote.transform)}){var dp=Pedestal(current,pair.Item1,dstExclude);var sp=Pedestal(source,pair.Item2,srcExclude);if(dp!=null&&sp!=null){CopyPose(dp,sp);report.Add(d.Terminal+" pedestal copied "+DevelopLayoutComparison.Path(dp));}else report.Add(d.Terminal+" pedestal left: unambiguous pair unavailable");}
 CopyPose(a.transform,b.transform);CopyPose(a.tote.transform,b.tote.transform);
 if(a.tote.slots.Length!=b.tote.slots.Length)throw new Exception("Slot count differs");
 for(int i=0;i<a.tote.slots.Length;i++)CopyPose(a.tote.slots[i],b.tote.slots[i]);
 // Container physical panels: match relative child paths, without replacing scripts/materials/references.
 foreach(var st in b.tote.GetComponentsInChildren<Transform>(true)){if(st==b.tote.transform)continue;var path=AnimationUtility.CalculateTransformPath(st,b.tote.transform);var dt=a.tote.transform.Find(path);if(dt==null){report.Add("Unmatched container child "+path);continue;}dt.localPosition=st.localPosition;dt.localRotation=st.localRotation;dt.localScale=st.localScale;EditorUtility.SetDirty(dt);PrefabUtility.RecordPrefabInstancePropertyModifications(dt);}
 a.readyAngles=(float[])b.readyAngles.Clone();EditorUtility.SetDirty(a);PrefabUtility.RecordPrefabInstancePropertyModifications(a);
 CopyPose(d.EndLine.StopPoint,s.EndLine.StopPoint);
 report.Add(d.Terminal+" robot "+DevelopLayoutComparison.Pose(a.transform));report.Add(d.Terminal+" tote "+DevelopLayoutComparison.Pose(a.tote.transform));
 }
 EditorSceneManager.CloseScene(source,true);
 foreach(var item in preserved)if(item.Key==null||EditorJsonUtility.ToJson(item.Key)!=item.Value)throw new Exception("UI/camera changed: "+item.Key);
 foreach(var d in dst)if(!d.ValidateConfiguration(out var error))throw new Exception(error);
 EditorSceneManager.MarkSceneDirty(current);if(!EditorSceneManager.SaveScene(current))throw new Exception("Save failed");
 report.Add("PASS: terminal configuration valid; UI and Camera components unchanged; scene saved. No runtime scripts replaced.");File.WriteAllLines("Logs/ApplyDevelopTerminalLayout.txt",report);AssetDatabase.DeleteAsset("Assets/LayoutComparisonTemp");EditorApplication.Exit(0);
 }catch(Exception e){report.Add("FAIL "+e);File.WriteAllLines("Logs/ApplyDevelopTerminalLayout.txt",report);EditorApplication.Exit(1);}}
}
