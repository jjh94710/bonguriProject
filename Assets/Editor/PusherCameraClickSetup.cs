using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class PusherCameraClickSetup {
 static PusherCameraClickSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/PusherCameraClick.request"))return;File.Delete("Temp/PusherCameraClick.request");try{
 var root=GameObject.Find("CameraNavigationUI");var scene=root.scene;var camera=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Single(c=>c.gameObject.scene==scene&&c.name=="CameraBoxPusherTop");
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/PusherCamera/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 var t=root.transform.Find("PusherClickArea");GameObject g;if(t==null){g=new GameObject("PusherClickArea",typeof(RectTransform),typeof(Image),typeof(Button));g.transform.SetParent(root.transform,false);}else g=t.gameObject;
 var r=g.GetComponent<RectTransform>();r.anchorMin=new Vector2(.435f,.49f);r.anchorMax=new Vector2(.52f,.58f);r.offsetMin=r.offsetMax=Vector2.zero;var image=g.GetComponent<Image>();image.color=Color.clear;image.raycastTarget=true;var button=g.GetComponent<Button>();button.transition=Selectable.Transition.None;button.onClick=new Button.ButtonClickedEvent();g.transform.SetAsLastSibling();
 var so=new SerializedObject(root.GetComponent<FirstConveyorViewSwitch>());so.FindProperty("pusherCamera").objectReferenceValue=camera;so.FindProperty("pusherButton").objectReferenceValue=button;so.ApplyModifiedPropertiesWithoutUndo();camera.enabled=false;var listener=camera.GetComponent<AudioListener>();if(listener!=null)listener.enabled=false;
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText("Temp/PusherCameraClickResult.txt","OK: PusherClickArea -> CameraBoxPusherTop; Back returns overview; saved");
 }catch(Exception e){File.WriteAllText("Temp/PusherCameraClickResult.txt",e.ToString());Debug.LogException(e);}}
}
