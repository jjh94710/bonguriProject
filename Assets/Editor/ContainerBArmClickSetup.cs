using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class ContainerBArmClickSetup {
 const string Key="ContainerBArmSnapshot";
 [Serializable] class Snapshot { public Vector3 position,scale; public Quaternion rotation; public string parent; public float fov,near,far; public int mask,display; public Color background; public CameraClearFlags clear; }
 static ContainerBArmClickSetup(){EditorApplication.update+=Poll;}
 static void Poll(){
 if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
 try {
 if(File.Exists("Temp/ContainerBArmClick.request")){
 var c=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).FirstOrDefault(x=>x.name=="CameraB_Arm"&&x.gameObject.scene.IsValid());
 if(c==null){File.Delete("Temp/ContainerBArmClick.request");File.WriteAllText("Temp/ContainerBArmClickResult.txt","MISSING CameraB_Arm; no camera position guessed");return;}
 var t=c.transform; var data=new Snapshot{position=t.position,rotation=t.rotation,scale=t.localScale,parent=t.parent==null?"":AnimationUtility.CalculateTransformPath(t.parent,null),fov=c.fieldOfView,near=c.nearClipPlane,far=c.farClipPlane,mask=c.cullingMask,display=c.targetDisplay,background=c.backgroundColor,clear=c.clearFlags};
 SessionState.SetString(Key,JsonUtility.ToJson(data));File.Delete("Temp/ContainerBArmClick.request");
 if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}
 }
 if(EditorApplication.isPlayingOrWillChangePlaymode)return;
 var json=SessionState.GetString(Key,"");if(string.IsNullOrEmpty(json))return;
 var root=GameObject.Find("CameraNavigationUI");if(root==null)return;
 var scene=root.scene;var data2=JsonUtility.FromJson<Snapshot>(json);
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/ContainerBArm/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 var cam=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).FirstOrDefault(x=>x.name=="CameraB_Arm"&&x.gameObject.scene==scene);
 if(cam==null){var source=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Single(x=>x.name=="CameraContainerB"&&x.gameObject.scene==scene);cam=UnityEngine.Object.Instantiate(source);cam.name="CameraB_Arm";var parent=string.IsNullOrEmpty(data2.parent)?null:GameObject.Find(data2.parent);cam.transform.SetParent(parent==null?null:parent.transform,false);cam.transform.SetPositionAndRotation(data2.position,data2.rotation);cam.transform.localScale=data2.scale;cam.fieldOfView=data2.fov;cam.nearClipPlane=data2.near;cam.farClipPlane=data2.far;cam.cullingMask=data2.mask;cam.targetDisplay=data2.display;cam.backgroundColor=data2.background;cam.clearFlags=data2.clear;}
 var child=root.transform.Find("ContainerBArmClickArea");var g=child==null?new GameObject("ContainerBArmClickArea",typeof(RectTransform),typeof(Image),typeof(Button)):child.gameObject;g.transform.SetParent(root.transform,false);
 var rect=g.GetComponent<RectTransform>();rect.anchorMin=new Vector2(.425f,.335f);rect.anchorMax=new Vector2(.515f,.44f);rect.offsetMin=rect.offsetMax=Vector2.zero;
 var img=g.GetComponent<Image>();img.color=Color.clear;img.raycastTarget=true;var button=g.GetComponent<Button>();button.transition=Selectable.Transition.None;g.SetActive(false);
 var so=new SerializedObject(root.GetComponent<FirstConveyorViewSwitch>());so.FindProperty("containerBArmCamera").objectReferenceValue=cam;so.FindProperty("containerBArmButton").objectReferenceValue=button;so.ApplyModifiedPropertiesWithoutUndo();cam.enabled=false;var listener=cam.GetComponent<AudioListener>();if(listener!=null)listener.enabled=false;
 EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed");SessionState.EraseString(Key);File.WriteAllText("Temp/ContainerBArmClickResult.txt","OK saved "+scene.path+"; CameraContainerB -> CameraB_Arm; Back -> CameraContainerB; camera world pose preserved");
 }catch(Exception e){SessionState.EraseString(Key);File.WriteAllText("Temp/ContainerBArmClickResult.txt",e.ToString());Debug.LogException(e);}
 }
}