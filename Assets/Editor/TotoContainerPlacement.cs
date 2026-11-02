using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad]
public static class TotoContainerPlacement
{
 static TotoContainerPlacement(){EditorApplication.update+=Poll;}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!File.Exists("Temp/TotoContainerPlacement.request"))return;File.Delete("Temp/TotoContainerPlacement.request");Run();}
 static void Run(){try{
 var t=UnityEngine.Object.FindObjectsByType<TotoBase>(FindObjectsSortMode.None).Single(x=>x.loader!=null);var a=t.loader;var box=a.targetBox;var k=new ArmKinematics(a.transform,a.tcp);var initial=k.CurrentAngles();var old=t.transform.position;var oldRot=t.transform.rotation;
 var axis=a.tcp.InverseTransformDirection((a.tcp.position-a.suctionCup.position).normalized);var down=Quaternion.FromToRotation(a.tcp.rotation*axis,Vector3.down)*a.tcp.rotation;
 Vector3 best=old;Quaternion bestRot=oldRot;float bestScore=float.PositiveInfinity;int valid=0;
 var offsets=new[]{new Vector2(-.30f,-.22f),new Vector2(-.32f,0),new Vector2(-.25f,.25f),new Vector2(0,.35f),new Vector2(.25f,.25f),new Vector2(0,-.35f),new Vector2(.25f,-.25f),new Vector2(-.40f,0)};
 foreach(var o in offsets)foreach(float baseY in new[]{box.bounds.min.y-.06f,box.bounds.min.y-.12f,old.y}){
 var pos=new Vector3(a.transform.position.x+o.x,baseY,a.transform.position.z+o.y);
 // Leave all robot and conveyor collider geometry intact; require a free tote volume.
 var blockers=Physics.OverlapBox(pos+Vector3.up*.086f,new Vector3(.079f,.085f,.089f),Quaternion.identity).Where(c=>!c.isTrigger&&!c.transform.IsChildOf(t.transform)&&c.gameObject.activeInHierarchy).ToArray();
 if(blockers.Length>0)continue;
 foreach(float yaw in new[]{0f,90f}){var rot=Quaternion.Euler(0,yaw,0);bool ok=true;float cost=0;var seed=(float[])initial.Clone();
 foreach(var slot in t.slots){var local=t.transform.InverseTransformPoint(slot.position);var centre=pos+rot*local;var contact=centre+Vector3.up*.036f;float carryY=Mathf.Max(box.bounds.max.y+.16f,pos.y+.172f+.16f+.035f);
 foreach(var point in new[]{new Vector3(contact.x,carryY,contact.z),contact}){
 if(!k.Solve(point,down,seed,out var q,out float pe,out float ae,2f,z=>k.HasRaisedElbow(z,point.y+.10f)&&k.WristAbovePlane(z,box.bounds.min.y+.001f),.0015f)){ok=false;break;}
 cost+=pe;seed=q;}
 if(!ok)break;}
 if(!ok)continue;valid++;float score=Vector3.Distance(pos,box.bounds.center)+Vector3.Distance(pos,a.transform.position)*.2f+cost*10;
 if(score<bestScore){bestScore=score;best=pos;bestRot=rot;}}
 }
 if(valid==0){File.WriteAllText("Temp/TotoContainerPlacementResult.txt","FAIL no free location reached all eight slots; original retained");return;}
 string backup=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"/UnityBackups/bonguriProject/TotePosition/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));EditorSceneManager.SaveScene(t.gameObject.scene,backup,true);
 Undo.RecordObject(t.transform,"Move tote into reachable workspace");t.transform.SetPositionAndRotation(best,bestRot);PrefabUtility.RecordPrefabInstancePropertyModifications(t.transform);EditorSceneManager.MarkSceneDirty(t.gameObject.scene);EditorSceneManager.SaveScene(t.gameObject.scene);Selection.activeGameObject=t.gameObject;if(SceneView.lastActiveSceneView!=null)SceneView.lastActiveSceneView.Frame(new Bounds((best+box.bounds.center)*.5f,Vector3.one*.9f),false);
 File.WriteAllText("Temp/TotoContainerPlacementResult.txt","OK old="+old+" new="+best+" rotation="+bestRot.eulerAngles+" reachableCandidates="+valid+" slots=8 backup="+backup);
 }catch(Exception e){File.WriteAllText("Temp/TotoContainerPlacementResult.txt",e.ToString());}}
}
