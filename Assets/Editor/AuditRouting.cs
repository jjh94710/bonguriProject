using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class AuditRouting {
public static void Run(){try{EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");var report=new System.Collections.Generic.List<string>();foreach(var p in UnityEngine.Object.FindObjectsByType<BoxPusher>(FindObjectsSortMode.None)){var s=new SerializedObject(p);report.Add(p.name+" pos="+p.transform.position+" forward="+p.transform.forward+" target="+s.FindProperty("target").intValue+" divert="+s.FindProperty("divertPoint").objectReferenceValue+" line="+s.FindProperty("toLine").objectReferenceValue);}
foreach(var l in UnityEngine.Object.FindObjectsByType<ConveyorLine>(FindObjectsSortMode.None)){var s=new SerializedObject(l);report.Add(l.name+" component="+l.name);foreach(string f in new[]{"entry","exit","stopPoint","nextLine"}){var c=s.FindProperty(f).objectReferenceValue as Component;report.Add(f+"="+(c==null?"null":c.name+" "+c.transform.position));}}
var bin=GameObject.Find("RejectCollectionBox");foreach(var c in bin.GetComponentsInChildren<Collider>())report.Add("BIN "+c.name+" bounds="+c.bounds);
File.WriteAllLines("Logs/RouteAudit.txt",report);EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}}
}

