using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class ContainerBCCameraClickSetup {
 static ContainerBCCameraClickSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/ContainerBCCameraClick.request"))return;File.Delete("Temp/ContainerBCCameraClick.request");try{
 var root=GameObject.Find("CameraNavigationUI");var scene=root.scene;var cams=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Where(c=>c.gameObject.scene==scene).ToArray();var b=cams.Single(c=>c.name=="CameraContainerB");var c=cams.Single(ca=>ca.name=="CameraContainerC");
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/ContainerBC/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 var bb=Make(root.transform,"ContainerBClickArea",new Vector2(.435f,.58f),new Vector2(.515f,.715f));var cb=Make(root.transform,"ContainerCClickArea",new Vector2(.615f,.565f),new Vector2(.725f,.725f));
 var so=new SerializedObject(root.GetComponent<FirstConveyorViewSwitch>());so.FindProperty("containerBCamera").objectReferenceValue=b;so.FindProperty("containerCCamera").objectReferenceValue=c;so.FindProperty("containerBButton").objectReferenceValue=bb;so.FindProperty("containerCButton").objectReferenceValue=cb;so.ApplyModifiedPropertiesWithoutUndo();foreach(var camera in new[]{b,c}){camera.enabled=false;var listener=camera.GetComponent<AudioListener>();if(listener!=null)listener.enabled=false;}
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText("Temp/ContainerBCCameraClickResult.txt","OK: center -> CameraContainerB; right -> CameraContainerC; Back and HUD preserved; saved");
 }catch(Exception e){File.WriteAllText("Temp/ContainerBCCameraClickResult.txt",e.ToString());Debug.LogException(e);}}
 static Button Make(Transform root,string name,Vector2 min,Vector2 max){var t=root.Find(name);GameObject g;if(t==null){g=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));g.transform.SetParent(root,false);}else g=t.gameObject;var r=g.GetComponent<RectTransform>();r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;var image=g.GetComponent<Image>();image.color=Color.clear;image.raycastTarget=true;var button=g.GetComponent<Button>();button.transition=Selectable.Transition.None;button.onClick=new Button.ButtonClickedEvent();g.transform.SetAsLastSibling();return button;}
}
