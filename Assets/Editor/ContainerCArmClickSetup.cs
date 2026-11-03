using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class ContainerCArmClickSetup {
 static ContainerCArmClickSetup(){EditorApplication.update+=Poll;}
 static void Poll(){
 if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/ContainerCArmClick.request"))return;
 if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}if(EditorApplication.isPlayingOrWillChangePlaymode)return;
 File.Delete("Temp/ContainerCArmClick.request");
 try {
 var root=GameObject.Find("CameraNavigationUI");var scene=root.scene;
 var source=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Single(x=>x.name=="CameraContainerC"&&x.gameObject.scene==scene);
 var robots=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include).Where(x=>x!=null&&x.GetType().Name=="BackArmIK"&&x.gameObject.scene==scene).ToArray();
 if(robots.Length==0)throw new Exception("No BackArmIK robots found");
 var robot=robots.Where(x=>source.WorldToViewportPoint(x.transform.position).z>0).OrderBy(x=>{var v=source.WorldToViewportPoint(x.transform.position);return Vector2.SqrMagnitude(new Vector2(v.x,v.y)-new Vector2(.48f,.36f));}).First();
 var renderers=robot.GetComponentsInChildren<Renderer>().Where(x=>x.enabled).ToArray();if(renderers.Length==0)throw new Exception("Robot has no renderers");
 var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/ContainerCArm/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 var cam=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).FirstOrDefault(x=>x.name=="CameraC_Arm"&&x.gameObject.scene==scene);
 if(cam==null){cam=UnityEngine.Object.Instantiate(source,source.transform.parent);cam.name="CameraC_Arm";}
 cam.fieldOfView=55;cam.nearClipPlane=.03f;cam.orthographic=false;
 float distance=Mathf.Max(1.5f,Mathf.Max(bounds.size.z,bounds.size.x)*1.5f);
 cam.transform.position=new Vector3(bounds.center.x,bounds.max.y+distance,bounds.center.z);
 var screenUp=Vector3.ProjectOnPlane(source.transform.forward,Vector3.up);if(screenUp.sqrMagnitude<.001f)screenUp=Vector3.forward;
 cam.transform.rotation=Quaternion.LookRotation(Vector3.down,screenUp.normalized);
 var child=root.transform.Find("ContainerCArmClickArea");var g=child==null?new GameObject("ContainerCArmClickArea",typeof(RectTransform),typeof(Image),typeof(Button)):child.gameObject;g.transform.SetParent(root.transform,false);
 var rect=g.GetComponent<RectTransform>();rect.anchorMin=new Vector2(.425f,.32f);rect.anchorMax=new Vector2(.53f,.43f);rect.offsetMin=rect.offsetMax=Vector2.zero;
 var img=g.GetComponent<Image>();img.color=Color.clear;img.raycastTarget=true;var button=g.GetComponent<Button>();button.transition=Selectable.Transition.None;g.SetActive(false);
 var so=new SerializedObject(root.GetComponent<FirstConveyorViewSwitch>());so.FindProperty("containerCArmCamera").objectReferenceValue=cam;so.FindProperty("containerCArmButton").objectReferenceValue=button;so.ApplyModifiedPropertiesWithoutUndo();cam.enabled=false;var listener=cam.GetComponent<AudioListener>();if(listener!=null)listener.enabled=false;
 EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed");File.WriteAllText("Temp/ContainerCArmClickResult.txt","OK saved "+scene.path+" robot="+robot.name+" world="+robot.transform.position+" bounds="+bounds+" camera="+cam.transform.position+"; C -> C_Arm; Back -> C");
 }catch(Exception e){File.WriteAllText("Temp/ContainerCArmClickResult.txt",e.ToString());Debug.LogException(e);}
 }
}