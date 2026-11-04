using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad]
public static class CorrectionReleaseRegression
{
 const string Key="CorrectionReleaseRegression.Run";
 static double started;
 static CorrectionReleaseRegression(){if(SessionState.GetBool(Key,false))Hook();}
 static void Hook(){started=EditorApplication.timeSinceStartup;EditorApplication.update-=Tick;EditorApplication.update+=Tick;}
 public static void Run(){EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");SessionState.SetBool(Key,true);Hook();EditorApplication.isPlaying=true;}
 static void Tick(){if(EditorApplication.timeSinceStartup-started>60){Finish(false,"startup timeout");return;}if(!EditorApplication.isPlaying||Time.time<1)return;EditorApplication.update-=Tick;
 try{
 var cell=UnityEngine.Object.FindFirstObjectByType<CellController>();
 var line=(ConveyorLine)new SerializedObject(cell).FindProperty("correctLine").objectReferenceValue;
 var arrive=typeof(ConveyorLine).GetMethod("Arrive",BindingFlags.NonPublic|BindingFlags.Instance);
 var release=typeof(CellController).GetMethod("TryReleaseCorrectionBox",BindingFlags.NonPublic|BindingFlags.Instance);
 BoxInfo Make(string id){var go=new GameObject(id);go.transform.position=line.StopPoint.position;var box=go.AddComponent<BoxInfo>();box.boxId=id;return box;}
 var first=Make("RELEASE-001");var second=Make("RELEASE-002");
 arrive.Invoke(line,new object[]{first});
 Require((bool)release.Invoke(cell,new object[]{first}),"first release accepted");
 Require(line.CurrentBox==null,"only current parcel cleared");
 Require(!(bool)release.Invoke(cell,new object[]{first}),"duplicate release blocked");
 arrive.Invoke(line,new object[]{second});var original=second.state;
 Require(!(bool)release.Invoke(cell,new object[]{first}),"stale timeout cannot release next parcel");
 Require(line.CurrentBox==second&&second.state==original,"next parcel remains waiting");
 Require((bool)release.Invoke(cell,new object[]{second}),"next parcel independently releasable");
 Require(!(bool)release.Invoke(cell,new object[]{second}),"next duplicate blocked");
 Finish(true,"PASS: one current parcel released; duplicate and stale parcel releases blocked; next parcel remains waiting until its own release. Guard exercised in Play; full 90-second robot cycle not exercised.");
 }catch(Exception e){Finish(false,e.ToString());}}
 static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Finish(bool ok,string report){SessionState.SetBool(Key,false);EditorApplication.update-=Tick;File.WriteAllText("Logs/CorrectionReleaseRegression.txt",report);EditorApplication.Exit(ok?0:1);}
}
