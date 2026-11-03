using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class FirstConveyorCameraSetup {
 static FirstConveyorCameraSetup(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/FirstConveyorCamera.request"))return;File.Delete("Temp/FirstConveyorCamera.request");try{
 var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();var cameras=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Where(c=>c.gameObject.scene==scene).ToArray();var first=cameras.Single(c=>c.name=="CameraFirstConveyor");var hud=GameObject.Find("FactoryHUD");if(hud==null)throw new Exception("FactoryHUD missing");
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/FirstCamera/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 var canvas=hud.GetComponent<Canvas>();int display=first.targetDisplay;canvas.targetDisplay=display;first.gameObject.SetActive(true);first.targetDisplay=display;first.enabled=true;
 var others=cameras.Where(c=>c!=first&&c.targetTexture==null&&(c.transform.parent==first.transform.parent||c.name.StartsWith("CameraSideUi")||c.name=="TopCamera")).ToArray();foreach(var c in others){c.enabled=false;var l=c.GetComponent<AudioListener>();if(l!=null)l.enabled=false;}
 var listener=first.GetComponent<AudioListener>();if(listener!=null)listener.enabled=true;
 foreach(var panel in hud.GetComponentsInChildren<BoxInfoPanel>(true)){var p=new SerializedObject(panel);var a=p.FindProperty("viewCameras");a.arraySize=1;a.GetArrayElementAtIndex(0).objectReferenceValue=first;p.ApplyModifiedPropertiesWithoutUndo();panel.Refresh();}
 var root=GameObject.Find("CameraNavigationUI");if(root!=null){var nav=root.GetComponent<SideCameraNavigation>();var so=new SerializedObject(nav);var views=so.FindProperty("views");views.arraySize=1;views.GetArrayElementAtIndex(0).objectReferenceValue=first;var arr=so.FindProperty("otherViewCameras");arr.arraySize=others.Length;for(int i=0;i<others.Length;i++)arr.GetArrayElementAtIndex(i).objectReferenceValue=others[i];so.ApplyModifiedPropertiesWithoutUndo();foreach(string n in new[]{"LeftViewButton","RightViewButton","TopCameraButton"}){var t=root.transform.Find(n);if(t!=null)t.gameObject.SetActive(false);}var nc=root.GetComponent<Canvas>();if(nc!=null)nc.targetDisplay=display;}
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText("Temp/FirstConveyorCameraResult.txt","OK: CameraFirstConveyor startup; HUD display="+display+"; previous navigation buttons hidden; scene saved");
 }catch(Exception e){File.WriteAllText("Temp/FirstConveyorCameraResult.txt",e.ToString());Debug.LogException(e);}}
}

