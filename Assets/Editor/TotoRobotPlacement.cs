using System;using System.IO;using System.Linq;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;
[InitializeOnLoad]public static class TotoRobotPlacement
{
 static TotoRobotPlacement(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/TotoPlacement.request"))return;File.Delete("Temp/TotoPlacement.request");Run();}
 static void Run(){
 var tote=UnityEngine.Object.FindFirstObjectByType<ToteBase>();var a=tote.loader;var box=a.targetBox;var ik=a.armIK;
 var original=a.transform.position;var scene=a.gameObject.scene;
 var stand=UnityEngine.Object.FindObjectsByType<BoxCollider>().Where(c=>!c.transform.IsChildOf(a.transform)&&c.bounds.size.y>.25f&&c.bounds.size.y<.8f&&Vector2.Distance(new Vector2(c.bounds.center.x,c.bounds.center.z),new Vector2(original.x,original.z))<.12f).OrderBy(c=>Mathf.Abs(c.bounds.max.y-original.y)).FirstOrDefault();
 var axis=a.tcp.InverseTransformDirection((a.tcp.position-a.suctionCup.position).normalized);
 var offsets=new[]{Vector3.zero,new Vector3(0,-.12f,0),new Vector3(0,-.20f,0),new Vector3(0,-.12f,-.20f),new Vector3(0,-.12f,.20f),new Vector3(-.10f,-.12f,-.20f),new Vector3(-.10f,-.12f,.20f),new Vector3(0,-.20f,-.30f),new Vector3(0,-.20f,.30f),new Vector3(-.15f,-.20f,-.15f),new Vector3(-.15f,-.20f,.15f),new Vector3(.10f,-.12f,-.20f),new Vector3(.10f,-.12f,.20f)};
 string report="";bool found=false;Vector3 chosen=original;
 foreach(var offset in offsets){a.transform.position=original+offset;Physics.SyncTransforms();ik.Configure(a.tcp,box,20,.07f,.003f,3,.006f,false);ik.HighPrecision=true;ik.minimumWristPlane=box.bounds.min.y+.001f;ik.raisedElbowClearance=.10f;
 foreach(float sign in new[]{1f,-1f}){var normal=box.transform.forward*sign;if(Vector3.Dot(normal,Vector3.up)<-.5f)continue;var point=tote.SuctionPoint(box,sign);var aligned=Quaternion.FromToRotation(a.tcp.rotation*axis,-normal)*a.tcp.rotation;
 foreach(float roll in new[]{0f,90f,-90f,180f}){var rotation=Quaternion.AngleAxis(roll,normal)*aligned;
 if(ik.PlanSuctionApproach(point+normal*a.clearance+Vector3.up*.12f,point+normal*a.clearance,point+normal*.001f,rotation,box,true)){found=true;chosen=a.transform.position;report="OK position="+chosen+" offset="+offset+" sign="+sign+" roll="+roll;break;}}
 if(found)break;}if(found)break;}
 a.transform.position=original;
 if(found){string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/TotoPlacement/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(scene,backup,true);
 Undo.RecordObject(a,"Store validated ready pose");a.readyAngles=new ArmKinematics(a.transform,a.tcp).CurrentAngles();EditorUtility.SetDirty(a);PrefabUtility.RecordPrefabInstancePropertyModifications(a);Undo.RecordObject(a.transform,"Adjust suction robot installation");a.transform.position=chosen;PrefabUtility.RecordPrefabInstancePropertyModifications(a.transform);
 if(stand!=null){Undo.RecordObject(stand.transform,"Adjust robot pedestal");var bottom=stand.bounds.min.y;float top=stand.bounds.max.y+(chosen.y-original.y);stand.transform.position+=new Vector3(chosen.x-original.x,0,chosen.z-original.z);if(top-bottom>.05f){var scale=stand.transform.localScale;scale.y*= (top-bottom)/stand.bounds.size.y;stand.transform.localScale=scale;var pos=stand.transform.position;pos.y+=(top-bottom-stand.bounds.size.y)*.5f;stand.transform.position=pos;}}
 EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);report+=" backup="+backup;
 }else report="FAIL no tested installation has a valid raised-elbow contact path";
 File.WriteAllText("Temp/TotoPlacementResult.txt",report);
 }
}

