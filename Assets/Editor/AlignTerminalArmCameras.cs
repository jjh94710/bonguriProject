using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad] public static class AlignTerminalArmCameras {
 static AlignTerminalArmCameras(){EditorApplication.update+=Poll;}
 static void Poll(){
 if(EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/AlignTerminalArmCameras.request"))return;
 if(EditorApplication.isPlaying){EditorApplication.isPlaying=false;return;}if(EditorApplication.isPlayingOrWillChangePlaymode)return;
 File.Delete("Temp/AlignTerminalArmCameras.request");
 try{
 var root=GameObject.Find("CameraNavigationUI");var scene=root.scene;
 var cameras=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Where(c=>c.gameObject.scene==scene).ToArray();
 var robots=UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include).Where(x=>x!=null&&x.GetType().Name=="BackArmIK"&&x.gameObject.scene==scene).ToArray();
 var selected=new System.Collections.Generic.List<MonoBehaviour>();var sources=new System.Collections.Generic.List<Camera>();
 foreach(string id in new[]{"A","B","C"}){
 var source=cameras.Single(c=>c.name=="CameraContainer"+id);sources.Add(source);
 var robot=robots.Where(x=>source.WorldToViewportPoint(x.transform.position).z>0).OrderBy(x=>{var v=source.WorldToViewportPoint(x.transform.position);return Vector2.SqrMagnitude(new Vector2(v.x,v.y)-new Vector2(.48f,.36f));}).First();selected.Add(robot);
 }
 if(selected.Distinct().Count()!=3)throw new Exception("Terminal robot matching is ambiguous; no cameras moved.");
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/AlignArmCameras/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 string report="";
 for(int i=0;i<2;i++){
 string id=i==0?"A":"B";var cam=cameras.Single(c=>c.name=="Camera"+id+"_Arm");var robot=selected[i];var renders=robot.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();if(renders.Length==0)throw new Exception("No robot renderers");var b=renders[0].bounds;foreach(var r in renders)b.Encapsulate(r.bounds);
 // Assign world pose: the camera parent may have a nonidentity transform.
 cam.transform.position=new Vector3(b.center.x,b.max.y+Mathf.Max(1.5f,Mathf.Max(b.size.x,b.size.z)*1.5f),b.center.z);
 var up=Vector3.ProjectOnPlane(sources[i].transform.forward,Vector3.up);if(up.sqrMagnitude<.001f)up=Vector3.forward;
 cam.transform.rotation=Quaternion.LookRotation(Vector3.down,up.normalized);cam.fieldOfView=55;cam.nearClipPlane=.03f;cam.farClipPlane=Mathf.Max(100,cam.farClipPlane);cam.orthographic=false;cam.targetDisplay=sources[i].targetDisplay;cam.enabled=false;
 var listener=cam.GetComponent<AudioListener>();if(listener!=null)listener.enabled=false;
 report+=id+" robot world="+robot.transform.position+" camera world="+cam.transform.position+" downward="+Vector3.Dot(cam.transform.forward,Vector3.down)+Environment.NewLine;
 }
 EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed");File.WriteAllText("Temp/AlignTerminalArmCamerasResult.txt","OK saved "+scene.path+Environment.NewLine+report);
 }catch(Exception e){File.WriteAllText("Temp/AlignTerminalArmCamerasResult.txt",e.ToString());Debug.LogException(e);}
 }
}