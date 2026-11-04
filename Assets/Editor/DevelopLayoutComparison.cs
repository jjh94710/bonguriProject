using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class DevelopLayoutComparison {
 public static void Run(){var rows=new List<string>();try{
 var current=EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");var source=EditorSceneManager.OpenScene("Assets/LayoutComparisonTemp/DevelopLayout.unity",OpenSceneMode.Additive);
 foreach(var scene in new[]{current,source}){
 rows.Add("SCENE "+scene.path);
 var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LoadSequence>(true)).OrderBy(x=>x.Terminal).ToArray();
 foreach(var s in all){var a=s.Attacher;rows.Add(s.Terminal+" robot="+Path(a.transform)+" pose="+Pose(a.transform)+" ready="+string.Join(",",a.readyAngles));rows.Add("tote="+Path(a.tote.transform)+" "+Pose(a.tote.transform));rows.Add("stop="+Path(s.EndLine.StopPoint)+" "+Pose(s.EndLine.StopPoint));foreach(var t in a.tote.slots)rows.Add("slot "+t.name+" "+Pose(t));}
 }
 File.WriteAllLines("Logs/DevelopLayoutComparison.txt",rows);EditorApplication.Exit(0);
 }catch(Exception e){File.WriteAllText("Logs/DevelopLayoutComparison.txt",e.ToString());EditorApplication.Exit(1);}}
 public static string Path(Transform t)=>t.parent==null?t.name:Path(t.parent)+"/"+t.name;
 public static string Pose(Transform t)=>"pos="+t.position.ToString("F4")+" rot="+t.rotation.eulerAngles.ToString("F2")+" scale="+t.lossyScale.ToString("F3");
}
