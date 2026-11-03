using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class ContainerACameraClickSetup {
 static ContainerACameraClickSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/ContainerACameraClick.request"))return;File.Delete("Temp/ContainerACameraClick.request");try{
 var root=GameObject.Find("CameraNavigationUI");var scene=root.scene;var camera=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Single(c=>c.gameObject.scene==scene&&c.name=="CameraContainerA");
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/ContainerACamera/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 var t=root.transform.Find("ContainerAClickArea");GameObject g;if(t==null){g=new GameObject("ContainerAClickArea",typeof(RectTransform),typeof(Image),typeof(Button));g.transform.SetParent(root.transform,false);}else g=t.gameObject;
 var r=g.GetComponent<RectTransform>();r.anchorMin=new Vector2(.21f,.54f);r.anchorMax=new Vector2(.34f,.71f);r.offsetMin=r.offsetMax=Vector2.zero;var image=g.GetComponent<Image>();image.color=Color.clear;image.raycastTarget=true;var button=g.GetComponent<Button>();button.transition=Selectable.Transition.None;button.onClick=new Button.ButtonClickedEvent();g.transform.SetAsLastSibling();
 var so=new SerializedObject(root.GetComponent<FirstConveyorViewSwitch>());so.FindProperty("containerACamera").objectReferenceValue=camera;so.FindProperty("containerAButton").objectReferenceValue=button;so.ApplyModifiedPropertiesWithoutUndo();camera.enabled=false;var listener=camera.GetComponent<AudioListener>();if(listener!=null)listener.enabled=false;
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText("Temp/ContainerACameraClickResult.txt","OK: ContainerAClickArea -> CameraContainerA; Back returns overview; scene saved");
 }catch(Exception e){File.WriteAllText("Temp/ContainerACameraClickResult.txt",e.ToString());Debug.LogException(e);}}
}
