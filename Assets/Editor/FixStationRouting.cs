using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class FixStationRouting {
public static void Run(){try {
var scene=EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");
var lines=UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None);
var correct=lines.Single(l=>l.name=="CorrectLine");var cs=new SerializedObject(correct);cs.FindProperty("stationApproachClearance").floatValue=.35f;cs.FindProperty("gap").floatValue=.12f;cs.ApplyModifiedPropertiesWithoutUndo();
var pushers=UnityEngine.Object.FindObjectsByType<BoxPusher>(FindObjectsSortMode.None);
var ng=pushers.Single(p=>p.name=="SeparatorC");var toC=pushers.Single(p=>p.name=="SeparatorA");var toA=pushers.Single(p=>p.name=="SeparatorB");
var tote=GameObject.Find("RejectCollectionBox").GetComponent<NgTote>();if(tote==null)throw new Exception("NgTote missing");
Configure(ng,Destination.NG,null,tote,"SeparatorNG_Divert",Vector3.back,.85f);
Configure(toC,Destination.C,lines.Single(l=>l.name=="TerminalC"),null,"SeparatorC_Divert",Vector3.forward,.85f);
Configure(toA,Destination.A,lines.Single(l=>l.name=="TerminalA"),null,"SeparatorA_Divert",Vector3.back,.85f);
var sort=lines.Single(l=>l.name=="SortLine");var ss=new SerializedObject(sort);var array=ss.FindProperty("pushers");array.arraySize=3;array.GetArrayElementAtIndex(0).objectReferenceValue=ng;array.GetArrayElementAtIndex(1).objectReferenceValue=toA;array.GetArrayElementAtIndex(2).objectReferenceValue=toC;ss.ApplyModifiedPropertiesWithoutUndo();
var report=new System.Collections.Generic.List<string>{"CorrectLine: working clearance .35m; queue gap .12m"};
foreach(var p in new[]{ng,toA,toC}){var s=new SerializedObject(p);var d=(Transform)s.FindProperty("divertPoint").objectReferenceValue;var dir=s.FindProperty("worldPushDirection").vector3Value;var end=d.position+dir*s.FindProperty("pushDistance").floatValue;var line=s.FindProperty("toLine").objectReferenceValue as ConveyorLine;
if(line!=null){var entry=(Transform)new SerializedObject(line).FindProperty("entry").objectReferenceValue;if(Vector3.Distance(end,entry.position)>.03f)throw new Exception("Pusher endpoint misses receiver: "+p.name);}
else {var floor=tote.GetComponentsInChildren<BoxCollider>().Single(c=>c.name=="Floor");if(end.x<floor.bounds.min.x||end.x>floor.bounds.max.x||end.z<floor.bounds.min.z||end.z>floor.bounds.max.z)throw new Exception("NG endpoint outside bin");}
report.Add(p.name+" -> "+(line!=null?line.name:"NgTote")+"; endpoint "+end+" OK");}
EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllLines("Logs/StationRoutingFix.txt",report);EditorApplication.Exit(0);
}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
static void Configure(BoxPusher p,Destination target,ConveyorLine line,NgTote tote,string marker,Vector3 direction,float distance){var s=new SerializedObject(p);s.FindProperty("target").intValue=(int)target;s.FindProperty("toLine").objectReferenceValue=line;s.FindProperty("toTote").objectReferenceValue=tote;var d=GameObject.Find(marker).transform;d.position=new Vector3(p.transform.position.x,.8613f,2.192f);s.FindProperty("divertPoint").objectReferenceValue=d;s.FindProperty("pusherHead").objectReferenceValue=p.transform;s.FindProperty("worldPushDirection").vector3Value=direction;s.FindProperty("pushDistance").floatValue=distance;s.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(p);}
}
