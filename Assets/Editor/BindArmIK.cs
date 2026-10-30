using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad] public static class BindArmIK {
 static double ready;
 static BindArmIK(){ready=EditorApplication.timeSinceStartup+4;EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<ready||!File.Exists("Temp/BindArmIK.request"))return;File.Delete("Temp/BindArmIK.request");Bind();}
 [MenuItem("Tools/Bonguri/Bind Arm IK Components")]
 public static void Bind(){try{
 if(EditorApplication.isPlayingOrWillChangePlaymode)return;
 var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();string backup="Backups/ArmIKScene/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Backup failed");
 int count=0;foreach(var root in scene.GetRootGameObjects())foreach(var sequence in root.GetComponentsInChildren<CorrectionSequence>(true)){
 var ik=sequence.GetComponent<ArmIK>();if(ik==null)ik=Undo.AddComponent<ArmIK>(sequence.gameObject);Undo.RecordObject(ik,"Configure ArmIK");ik.tcp=sequence.tcp;
 var so=new SerializedObject(sequence);so.FindProperty("armIK").objectReferenceValue=ik;so.ApplyModifiedProperties();
 EditorUtility.SetDirty(ik);EditorUtility.SetDirty(sequence);PrefabUtility.RecordPrefabInstancePropertyModifications(sequence);PrefabUtility.RecordPrefabInstancePropertyModifications(ik);count++;
 }
 EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed");File.WriteAllText("Temp/BindArmIKResult.txt","OK count="+count+" backup="+backup);
 }catch(Exception e){File.WriteAllText("Temp/BindArmIKResult.txt","ERROR "+e);Debug.LogException(e);}}
}
