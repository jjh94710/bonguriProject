using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RoboLacyTech.Conveyors;
[InitializeOnLoad] public static class RestorePusherScene {
 static double ready;
 static RestorePusherScene() { ready=EditorApplication.timeSinceStartup+3; EditorApplication.update+=Poll; }
 static void Poll() {
 if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<ready||!File.Exists("Temp/RestorePusherScene.request"))return;
 File.Delete("Temp/RestorePusherScene.request"); Setup();
 }
 [MenuItem("Tools/Bonguri/Restore Pusher Plates In Current Scene")]
 public static void Setup() {
 if(EditorApplication.isPlayingOrWillChangePlaymode)return;
 try {
 var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
 string backup="Backups/PusherRestore/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";
 Directory.CreateDirectory(Path.GetDirectoryName(backup));
 if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Scene backup failed");
 var left=GameObject.Find("ConveyorBeltStraightMixed.029 (2)"); var right=GameObject.Find("ConveyorBeltStraightMixed.029 (3)");
 if(left==null||right==null)throw new Exception("Receiver belts missing");
 var lb=left.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name.Contains("LOD0")).bounds;
 var rb=right.GetComponentsInChildren<MeshRenderer>(true).First(r=>r.name.Contains("LOD0")).bounds;
 float centreZ=(lb.max.z+rb.min.z)*.5f, centreX=(lb.center.x+rb.center.x)*.5f, top=Mathf.Max(lb.max.y,rb.max.y);
 var station=GameObject.Find("PusherStation"); if(station==null){station=new GameObject("PusherStation");Undo.RegisterCreatedObjectUndo(station,"Restore pusher station");}
 var parcel=GameObject.Find("PusherTestParcel");
 if(parcel==null){
 var template=UnityEngine.Object.FindObjectsByType<Transform>().First(t=>t.Find("CardboardBody")!=null&&t.GetComponent<BoxCollider>()!=null);
 parcel=UnityEngine.Object.Instantiate(template.gameObject); parcel.name="PusherTestParcel"; Undo.RegisterCreatedObjectUndo(parcel,"Pusher test parcel");
 parcel.transform.SetParent(station.transform,true); parcel.transform.rotation=Quaternion.identity;
 parcel.transform.position=new Vector3(centreX,top+.15f,centreZ); Physics.SyncTransforms();
 parcel.transform.position+=Vector3.up*(top+.004f-parcel.GetComponent<BoxCollider>().bounds.min.y);
 }
 var a=Plate("SeparatorA",new Vector3(centreX,top+.07f,lb.max.z+.005f),station.transform);
 var b=Plate("SeparatorB",new Vector3(centreX,top+.07f,rb.min.z-.005f),station.transform);
 Configure(a,b,right,rb,parcel.GetComponent<BoxCollider>(),Vector3.forward);
 Configure(b,a,left,lb,parcel.GetComponent<BoxCollider>(),Vector3.back);
 foreach(var go in new[]{left,right}){var c=go.GetComponent<LinearConveyor>();EditorUtility.SetDirty(c);PrefabUtility.RecordPrefabInstancePropertyModifications(c);}
 EditorSceneManager.MarkSceneDirty(scene); if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed");
 Selection.activeGameObject=b; SceneView.lastActiveSceneView?.Frame(new Bounds(new Vector3(centreX,top,centreZ),new Vector3(1.5f,.8f,2.2f)),false);
 File.WriteAllText("Temp/PusherRestoreResult.txt","OK A="+a.transform.position+" B="+b.transform.position+" box="+parcel.transform.position+" backup="+backup);
 }catch(Exception e){File.WriteAllText("Temp/PusherRestoreResult.txt","ERROR "+e);Debug.LogException(e);}
 }
 static GameObject Plate(string name,Vector3 position,Transform parent){
 var go=GameObject.Find(name); if(go!=null)return go;
 go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; Undo.RegisterCreatedObjectUndo(go,"Restore pusher plate");
 go.transform.SetParent(parent,true); go.transform.position=position; go.transform.localScale=new Vector3(.77658f,.14202f,.01f);
 var matPath="Assets/Project/Conveyors/PusherPlate.mat"; var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
 if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=new Color(.85f,.55f,.08f);AssetDatabase.CreateAsset(material,matPath);}
 go.GetComponent<Renderer>().sharedMaterial=material;
 var body=go.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;go.AddComponent<BoxPusher>(); return go;
 }
 static void Configure(GameObject plate,GameObject other,GameObject belt,Bounds bounds,BoxCollider box,Vector3 dir){
 var conveyor=belt.GetComponent<LinearConveyor>(); if(conveyor==null)conveyor=Undo.AddComponent<LinearConveyor>(belt);
 var so=new SerializedObject(conveyor);so.FindProperty("motor.startRunning").boolValue=false;so.FindProperty("motor.baseSpeed").floatValue=.12f;so.ApplyModifiedProperties();
 var push=plate.GetComponent<BoxPusher>();Undo.RecordObject(push,"Configure restored pusher");
 push.parcel=box;push.receiver=conveyor;push.oppositeSeparator=other;push.direction=dir;push.startReceiverAfterReturn=false;
 push.receiverCenter=bounds.center;push.receiverSize=bounds.size;push.maxTravel=dir.z>0?bounds.min.z+.16f-plate.transform.position.z:plate.transform.position.z-(bounds.max.z-.16f);
 if(push.maxTravel<=0)throw new Exception("Pusher direction mismatch");
 if(belt.transform.Find("PusherReceiverSurface")==null){
 var surface=new GameObject("PusherReceiverSurface");Undo.RegisterCreatedObjectUndo(surface,"Restore belt support");surface.transform.SetParent(belt.transform,true);
 surface.transform.position=new Vector3(bounds.center.x,bounds.max.y-.008f,bounds.center.z);
 surface.AddComponent<BoxCollider>().size=new Vector3(bounds.size.x-.08f,.02f,bounds.size.z-.02f);
 }
 EditorUtility.SetDirty(push);
 }
}
