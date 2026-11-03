using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class ContainerAArmClickSetup {
 static ContainerAArmClickSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/ContainerAArmClick.request"))return;File.Delete("Temp/ContainerAArmClick.request");try{
 var root=GameObject.Find("CameraNavigationUI");var scene=root.scene;var cams=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Where(c=>c.gameObject.scene==scene).ToArray();var camera=cams.FirstOrDefault(c=>c.name=="CameraA_Arm");if(camera==null){var source=cams.Single(c=>c.name=="CameraContainerA");camera=UnityEngine.Object.Instantiate(source,source.transform.parent);camera.name="CameraA_Arm";camera.transform.localPosition=new Vector3(74.6f,-21.1f,126.9f);camera.transform.localRotation=Quaternion.Euler(80.248f,6.71f,10.026f);}
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/ContainerAArm/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 var t=root.transform.Find("ContainerAArmClickArea");GameObject g;if(t==null){g=new GameObject("ContainerAArmClickArea",typeof(RectTransform),typeof(Image),typeof(Button));g.transform.SetParent(root.transform,false);}else g=t.gameObject;
 var r=g.GetComponent<RectTransform>();r.anchorMin=new Vector2(.445f,.35f);r.anchorMax=new Vector2(.54f,.47f);r.offsetMin=r.offsetMax=Vector2.zero;var image=g.GetComponent<Image>();image.color=Color.clear;image.raycastTarget=true;var button=g.GetComponent<Button>();button.transition=Selectable.Transition.None;button.onClick=new Button.ButtonClickedEvent();g.transform.SetAsLastSibling();g.SetActive(false);
 var so=new SerializedObject(root.GetComponent<FirstConveyorViewSwitch>());so.FindProperty("containerAArmCamera").objectReferenceValue=camera;so.FindProperty("containerAArmButton").objectReferenceValue=button;so.ApplyModifiedPropertiesWithoutUndo();camera.enabled=false;var listener=camera.GetComponent<AudioListener>();if(listener!=null)listener.enabled=false;
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText("Temp/ContainerAArmClickResult.txt","OK: CameraContainerA click -> CameraA_Arm; Back -> CameraContainerA; saved");
 }catch(Exception e){File.WriteAllText("Temp/ContainerAArmClickResult.txt",e.ToString());Debug.LogException(e);}}
}

