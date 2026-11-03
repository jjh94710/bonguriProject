using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class DetailCameraButtonsSetup {
 static DetailCameraButtonsSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/DetailCameraButtons.request"))return;File.Delete("Temp/DetailCameraButtons.request");try{
 var root=GameObject.Find("CameraNavigationUI");var scene=root.scene;var cams=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Where(c=>c.gameObject.scene==scene).ToArray();var left=cams.Single(c=>c.name=="CameraFirstCameraLeft");var right=cams.Single(c=>c.name=="CameraFirstCameraRight");
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/DetailCameras/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 var lb=Make(root.transform,"DetailLeftButton",0,"<");var rb=Make(root.transform,"DetailRightButton",1,">");
 var so=new SerializedObject(root.GetComponent<FirstConveyorViewSwitch>());so.FindProperty("leftCamera").objectReferenceValue=left;so.FindProperty("rightCamera").objectReferenceValue=right;so.FindProperty("leftButton").objectReferenceValue=lb;so.FindProperty("rightButton").objectReferenceValue=rb;so.ApplyModifiedPropertiesWithoutUndo();
 left.enabled=false;right.enabled=false;EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText("Temp/DetailCameraButtonsResult.txt","OK: detail left/right cameras and buttons connected; Back unchanged; saved");
 }catch(Exception e){File.WriteAllText("Temp/DetailCameraButtonsResult.txt",e.ToString());Debug.LogException(e);}}
 static Button Make(Transform root,string name,int side,string arrow){var t=root.Find(name);GameObject g;if(t==null){g=UnityEngine.Object.Instantiate(root.Find("FirstConveyorBackButton").gameObject,root);g.name=name;}else g=t.gameObject;var b=g.GetComponent<Button>();b.onClick=new Button.ButtonClickedEvent();var r=g.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(side,.5f);r.pivot=new Vector2(side,.5f);r.anchoredPosition=new Vector2(side==0?24:-24,0);r.sizeDelta=new Vector2(64,88);var text=g.GetComponentInChildren<Text>(true);text.text=arrow;text.fontSize=38;text.raycastTarget=false;g.SetActive(false);return b;}
}
