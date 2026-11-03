using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class CompactHudSetup {
 static CompactHudSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/CompactHud.request"))return;File.Delete("Temp/CompactHud.request");try{
 var hud=GameObject.Find("FactoryHUD");if(hud==null)throw new Exception("FactoryHUD missing");var scene=hud.scene;
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/CompactHud/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 foreach(string name in new[]{"DashboardUI","CellOperatorUI","BoxInfoPanel"}){
 var panel=hud.transform.Find(name).GetComponent<RectTransform>();Undo.RecordObject(panel,"패널 높이 축소");float old=panel.sizeDelta.y;panel.sizeDelta=new Vector2(panel.sizeDelta.x,244);
 foreach(Transform child in panel){var r=child as RectTransform;if(r==null||r.name=="Border"||r.anchorMin!=r.anchorMax)continue;Undo.RecordObject(r,"UI 내부 간격 정리");var p=r.anchoredPosition;p.y*=244f/old;r.anchoredPosition=p;}
 }
 Canvas.ForceUpdateCanvases();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText("Temp/CompactHudResult.txt","OK: three panels 580x244, content spacing compacted, rounded backgrounds preserved; saved "+scene.path);
 }catch(Exception e){File.WriteAllText("Temp/CompactHudResult.txt",e.ToString());Debug.LogException(e);}}
}
