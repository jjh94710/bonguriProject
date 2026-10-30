using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad] public static class SetupSeparatorC {
 static double ready;
 static SetupSeparatorC(){ready=EditorApplication.timeSinceStartup+3;EditorApplication.update+=Poll;EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("C.RejectTest",false)){started=false;wall=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}};}
 static bool started;static BoxPusher push;static double wall;static Vector3 home;
 static void Poll(){
 if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.timeSinceStartup<ready)return;
 if(File.Exists("Temp/SetupSeparatorC.request")){File.Delete("Temp/SetupSeparatorC.request");Setup();}
 if(File.Exists("Temp/TestSeparatorC.request")){File.Delete("Temp/TestSeparatorC.request");SessionState.SetBool("C.RejectTest",true);EditorApplication.isPlaying=true;}
 }
 [MenuItem("Tools/Bonguri/Configure Separator C Reject")]
 public static void Setup(){try{
 if(EditorApplication.isPlayingOrWillChangePlaymode)return;
 var c=GameObject.Find("SeparatorC");if(c==null)throw new Exception("SeparatorC missing");var scene=c.scene;
 string backup=System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) + "/UnityBackups/bonguriProject/SeparatorCScene/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Backup failed");
 var p=c.GetComponent<BoxPusher>();if(p==null)p=Undo.AddComponent<BoxPusher>(c);Undo.RecordObject(p,"Configure reject pusher");
 if(c.GetComponent<Rigidbody>()==null)Undo.AddComponent<Rigidbody>(c);c.GetComponent<Rigidbody>().isKinematic=true;c.GetComponent<Rigidbody>().useGravity=false;
 var existing=UnityEngine.Object.FindObjectsByType<Transform>().Where(t=>t.name!="RejectTestParcel"&&t.Find("CardboardBody")!=null&&t.GetComponent<BoxCollider>()!=null&&Mathf.Abs(t.position.x-c.transform.position.x)<.2f&&Mathf.Abs(t.position.z-2.12f)<.25f).FirstOrDefault();
 var parcel=GameObject.Find("RejectTestParcel");if(existing!=null){if(parcel!=null)Undo.DestroyObjectImmediate(parcel);parcel=existing.gameObject;}
 if(parcel==null){var template=GameObject.Find("PusherTestParcel");if(template==null)throw new Exception("Test parcel missing");parcel=UnityEngine.Object.Instantiate(template);parcel.name="RejectTestParcel";Undo.RegisterCreatedObjectUndo(parcel,"Reject test parcel");parcel.transform.position=new Vector3(c.transform.position.x,template.transform.position.y,template.transform.position.z);}
 var bin=GameObject.Find("RejectCollectionBox");if(bin==null){bin=new GameObject("RejectCollectionBox");Undo.RegisterCreatedObjectUndo(bin,"Reject collection box");} if(bin.GetComponentsInChildren<Collider>(true).Length==0){
 Vector3 centre=new Vector3(c.transform.position.x,.15f,1.30f);
 var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/Project/Conveyors/RejectCollectionBox.mat");if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.color=new Color(.22f,.33f,.42f);AssetDatabase.CreateAsset(mat,"Assets/Project/Conveyors/RejectCollectionBox.mat");}
 Part(bin,"Floor",centre,new Vector3(.85f,.04f,.72f),mat);
 Part(bin,"LeftWall",centre+new Vector3(-.425f,.22f,0),new Vector3(.03f,.48f,.75f),mat);
 Part(bin,"RightWall",centre+new Vector3(.425f,.22f,0),new Vector3(.03f,.48f,.75f),mat);
 Part(bin,"FrontWall",centre+new Vector3(0,.22f,-.36f),new Vector3(.85f,.48f,.03f),mat);
 Part(bin,"BackWall",centre+new Vector3(0,.22f,.36f),new Vector3(.85f,.48f,.03f),mat);
 }
 p.mode=BoxPusher.PusherMode.RejectToBin;p.parcel=parcel.GetComponent<BoxCollider>();p.receiver=null;p.oppositeSeparator=null;p.direction=Vector3.back;p.maxTravel=1.16f;p.collectionBin=bin.transform;
 p.collectionCenter=new Vector3(c.transform.position.x,.40f,1.30f);p.collectionSize=new Vector3(.81f,.48f,.68f);p.dropTimeout=5;p.startReceiverAfterReturn=false;
 EditorUtility.SetDirty(p);EditorUtility.SetDirty(c.GetComponent<Rigidbody>());EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Selection.activeGameObject=c;
 SceneView.lastActiveSceneView?.Frame(new Bounds(parcel.transform.position+Vector3.down*.25f,new Vector3(1.8f,1.4f,2)),false);
 File.WriteAllText("Temp/SeparatorCSetup.txt","OK C="+c.transform.position+" parcel="+parcel.transform.position+" bin="+p.collectionCenter+" backup="+backup);
 }catch(Exception e){File.WriteAllText("Temp/SeparatorCSetup.txt","ERROR "+e);Debug.LogException(e);}}
 static void Part(GameObject parent,string name,Vector3 pos,Vector3 size,Material mat){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);Undo.RegisterCreatedObjectUndo(g,"Create collection box");g.name=name;g.transform.SetParent(parent.transform,true);g.transform.position=pos;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=mat;}
 static void Tick(){
 if(!EditorApplication.isPlaying){EditorApplication.update-=Tick;return;}Application.runInBackground=true;EditorApplication.QueuePlayerLoopUpdate();if(Time.time<.6f)return;
 if(!started){push=GameObject.Find("SeparatorC").GetComponent<BoxPusher>();home=push.transform.position;push.ReportInspection(false,false);started=true;}
 File.WriteAllText("Temp/SeparatorCProgress.txt","sim="+Time.time+" "+push.Status+" box="+push.parcel.bounds+" homeError="+Vector3.Distance(home,push.transform.position));
 if(!push.Busy||EditorApplication.timeSinceStartup-wall>90){bool passed=push.Completed&&push.ParcelInsideCollection()&&Vector3.Distance(home,push.transform.position)<.003f;
 File.WriteAllText("Temp/SeparatorCTest.txt","passed="+passed+" "+push.Status+" inBin="+push.ParcelInsideCollection()+" homeError="+Vector3.Distance(home,push.transform.position));SessionState.SetBool("C.RejectTest",false);EditorApplication.update-=Tick;if(passed)EditorApplication.isPaused=true;else EditorApplication.isPlaying=false;}
 }
}
