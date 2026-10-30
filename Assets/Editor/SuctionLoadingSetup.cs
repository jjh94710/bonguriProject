using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[CustomEditor(typeof(BoxAttacher))] public class BoxAttacherEditor:Editor {
 public override void OnInspectorGUI(){DrawDefaultInspector();var a=(BoxAttacher)target;EditorGUILayout.LabelField("적재",a.LoadedCount+" / 6");EditorGUILayout.HelpBox(a.Status,MessageType.Info);
 using(new EditorGUI.DisabledScope(!Application.isPlaying||a.Busy||a.Attached))if(GUILayout.Button("다음 박스 흡착 → 2×3 수거함 적재"))a.LoadNextBox();
 using(new EditorGUI.DisabledScope(!Application.isPlaying))if(GUILayout.Button("적재 중단 (흡착 유지)"))a.StopLoading();if(Application.isPlaying)Repaint();}
}
[InitializeOnLoad] public static class SuctionLoadingSetup {
 static double ready,wall;static BoxAttacher loader;static int last;static bool requested;static Vector3 source;static float nextAt;
 static SuctionLoadingSetup(){ready=EditorApplication.timeSinceStartup+4;EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("Suction.LoadTest",false)){loader=null;last=0;requested=false;wall=EditorApplication.timeSinceStartup;nextAt=0;EditorApplication.update+=Tick;}};}
 static void Poll(){if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<ready)return;
 if(File.Exists("Temp/SetupSuctionLoading.request")){File.Delete("Temp/SetupSuctionLoading.request");Setup();}
 if(File.Exists("Temp/TestSuctionLoading.request")){File.Delete("Temp/TestSuctionLoading.request");SessionState.SetBool("Suction.LoadTest",true);EditorApplication.isPlaying=true;}}
 [MenuItem("Tools/Bonguri/Configure Suction Loading")]
 public static void Setup(){try{
 var bin=GameObject.Find("ParcelContainer_2x3_6");var box=GameObject.Find("ParcelBox_ProcessVariant");if(bin==null||box==null)throw new Exception("Container/parcel missing");
 var robot=UnityEngine.Object.FindObjectsByType<Transform>().Where(t=>t.name.StartsWith("fr5_suction")&&t.GetComponentsInChildren<ArticulationBody>().Length>6).OrderBy(t=>Vector3.Distance(t.position,bin.transform.position)).First();
 var scene=robot.gameObject.scene;string backup=System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) + "/UnityBackups/bonguriProject/SuctionLoading/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Backup failed");
 var tcp=robot.GetComponentsInChildren<Transform>().First(t=>t.name=="tcp");var cup=robot.GetComponentsInChildren<Transform>().First(t=>t.name=="suction_cup_link");
 var ik=robot.GetComponent<ArmIK>();if(ik==null)ik=Undo.AddComponent<ArmIK>(robot.gameObject);
 var attacher=robot.GetComponent<BoxAttacher>();if(attacher==null)attacher=Undo.AddComponent<BoxAttacher>(robot.gameObject);
 var old=robot.GetComponent<CorrectionSequence>();if(old!=null){Undo.RecordObject(old,"Disable finger sequence on suction robot");old.enabled=false;}
 Undo.RecordObject(attacher,"Configure suction loading");Undo.RecordObject(ik,"Configure suction IK");ik.tcp=tcp;attacher.armIK=ik;attacher.tcp=tcp;attacher.suctionCup=cup;attacher.container=bin.transform;attacher.targetBox=box.GetComponent<BoxCollider>();
 attacher.slots=bin.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Slot_")).OrderBy(t=>t.name).ToArray();if(attacher.slots.Length!=6)throw new Exception("Need six slots");
 var model=new ArmKinematics(robot,tcp);var seed=model.CurrentAngles();Vector3 axis=tcp.InverseTransformDirection((tcp.position-cup.position).normalized);
 Quaternion down=Quaternion.FromToRotation(tcp.rotation*axis,Vector3.down)*tcp.rotation;
 Vector3 top=attacher.targetBox.bounds.center;top.y=attacher.targetBox.bounds.max.y+.001f+attacher.clearance;
 bool found=false;float[] answer=null;foreach(float yaw in new[]{0f,90f,-90f,180f}){var rotation=Quaternion.AngleAxis(yaw,Vector3.up)*down;if(model.Solve(top,rotation,seed,out answer,out _,out _,2)){down=rotation;found=true;break;}}
 if(!found)throw new Exception("Pickup ready pose is unreachable");model.ApplyEditorPose(answer);attacher.readyAngles=answer;
 EditorUtility.SetDirty(attacher);EditorUtility.SetDirty(ik);PrefabUtility.RecordPrefabInstancePropertyModifications(attacher);PrefabUtility.RecordPrefabInstancePropertyModifications(ik);
 foreach(var joint in model.Joints){EditorUtility.SetDirty(joint.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(joint.transform);}
 EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed");Selection.activeGameObject=robot.gameObject;
 File.WriteAllText("Temp/SuctionLoadingSetup.txt","OK robot="+robot.name+" ready="+string.Join(",",answer)+" tcp="+tcp.position+" source="+attacher.targetBox.bounds.center+" slots=6 backup="+backup);
 }catch(Exception e){File.WriteAllText("Temp/SuctionLoadingSetup.txt","ERROR "+e);Debug.LogException(e);}}
 static void Tick(){if(!EditorApplication.isPlaying){EditorApplication.update-=Tick;return;}EditorApplication.QueuePlayerLoopUpdate();Application.runInBackground=true;if(Time.time<.7f)return;
 try{
 if(loader==null){loader=UnityEngine.Object.FindObjectsByType<BoxAttacher>().First();source=loader.PickupPosition;
 var km=new ArmKinematics(loader.transform,loader.tcp);var q=km.CurrentAngles();km.Forward(q,out var fp,out var fr);
 File.WriteAllText("Temp/SuctionPoseDebug.txt","ready="+string.Join(",",loader.readyAngles)+" actual="+string.Join(",",q)+" tcp="+loader.tcp.position+" fk="+fp+" root="+loader.GetComponentsInChildren<ArticulationBody>().First(b=>b.isRoot).transform.position+" immovable="+loader.GetComponentsInChildren<ArticulationBody>().First(b=>b.isRoot).immovable);}
 File.WriteAllText("Temp/SuctionLoadingProgress.txt","sim="+Time.time+" loaded="+loader.LoadedCount+" attached="+loader.Attached+" "+loader.Status+" ik="+loader.armIK.Status+" tcp="+loader.tcp.position+" source="+loader.PickupPosition+" target="+loader.targetBox.bounds.center);
 if(loader.LoadedCount==6){Finish(true,"six placed");return;}
 if(loader.LoadedCount>last){last=loader.LoadedCount;File.AppendAllText("Temp/SuctionPlacedSlots.txt","placed="+last+" pos="+loader.PlacedBoxes[last-1].bounds.center+"\n");requested=false;nextAt=Time.time+1;
 var asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/ParcelBox_ProcessVariant.prefab");var next=UnityEngine.Object.Instantiate(asset,loader.PickupPosition,Quaternion.identity);next.name="SuctionTestParcel_"+(last+1);next.GetComponent<Rigidbody>().position=loader.PickupPosition;Physics.SyncTransforms();loader.targetBox=next.GetComponent<BoxCollider>();}
 if(!requested&&Time.time>=nextAt){loader.LoadNextBox();requested=true;}
 else if(requested&&!loader.Busy){Finish(false,loader.Status);return;}
 if(EditorApplication.timeSinceStartup-wall>600)Finish(false,"timeout");
 }catch(Exception e){Finish(false,e.ToString());}}
 static void Finish(bool passed,string message){EditorApplication.update-=Tick;SessionState.SetBool("Suction.LoadTest",false);File.WriteAllText("Temp/SuctionLoadingResult.txt","passed="+passed+" count="+(loader==null?0:loader.LoadedCount)+" "+message);if(passed)EditorApplication.isPaused=true;else EditorApplication.isPlaying=false;}
}
