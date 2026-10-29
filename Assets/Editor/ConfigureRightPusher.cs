using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RoboLacyTech.Conveyors;

public static class ConfigureRightPusher
{
    const string Report = "Temp/RightPusherSetup.json";
    [MenuItem("Tools/Bonguri/Configure Right Pusher")]
    public static void Setup()
    {
        try
        {
            var a=GameObject.Find("SeparatorA"); var b=GameObject.Find("SeparatorB");
            if(a==null || b==null)throw new Exception("SeparatorA/B missing");
            string backup="Backups/RightPusher/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"/bongyuri.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); EditorSceneManager.SaveScene(a.scene,backup,true);
            var left=b.GetComponent<ParcelPusher>();
            var belt=GameObject.Find("ConveyorBeltStraightMixed.029 (3)");
            var r=belt.GetComponentsInChildren<MeshRenderer>(true).First(v=>v.name.Contains("LOD0"));
            var conveyor=belt.GetComponent<LinearConveyor>(); if(conveyor==null)conveyor=Undo.AddComponent<LinearConveyor>(belt);
            var so=new SerializedObject(conveyor); so.FindProperty("motor.startRunning").boolValue=false;so.FindProperty("motor.baseSpeed").floatValue=.12f;so.ApplyModifiedProperties();
            var rb=a.GetComponent<Rigidbody>();if(rb==null)rb=Undo.AddComponent<Rigidbody>(a); rb.isKinematic=true;rb.useGravity=false;
            var p=a.GetComponent<ParcelPusher>();if(p==null)p=Undo.AddComponent<ParcelPusher>(a);
            p.parcel=left.parcel;p.receiver=conveyor;p.direction=Vector3.forward;p.oppositeSeparator=b;p.startReceiverAfterReturn=false;
            p.receiverCenter=r.bounds.center+Vector3.up*.002f;p.receiverSize=r.bounds.size;
            p.maxTravel=(r.bounds.min.z+.16f)-a.transform.position.z;
            if(p.maxTravel<=0)throw new Exception("Right belt direction mismatch");
            left.oppositeSeparator=a;left.startReceiverAfterReturn=false;
            if(belt.transform.Find("PusherReceiverSurface")==null)
            {
                var surface=new GameObject("PusherReceiverSurface");Undo.RegisterCreatedObjectUndo(surface,"Right belt contact surface");
                surface.transform.position=new Vector3(r.bounds.center.x,r.bounds.max.y-.008f,r.bounds.center.z);surface.transform.SetParent(belt.transform,true);
                surface.AddComponent<BoxCollider>().size=new Vector3(r.bounds.size.x-.08f,.02f,r.bounds.size.z-.02f);
            }
            foreach(var obj in new UnityEngine.Object[]{p,left,rb,conveyor}){EditorUtility.SetDirty(obj);if(PrefabUtility.IsPartOfPrefabInstance(obj))PrefabUtility.RecordPrefabInstancePropertyModifications(obj);}
            EditorSceneManager.MarkSceneDirty(a.scene);EditorSceneManager.SaveScene(a.scene);Selection.activeGameObject=a;
            File.WriteAllText(Report,"A -> +Z right belt; B -> -Z left belt; travel="+p.maxTravel+"; receivers stopped; backup="+backup);
        }
        catch(Exception e){File.WriteAllText(Report,"ERROR: "+e);Debug.LogException(e);}
    }
}
