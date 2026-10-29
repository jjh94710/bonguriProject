using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RoboLacyTech.Conveyors;

[CustomEditor(typeof(ParcelPusher))]
public class ParcelPusherEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); var p = (ParcelPusher)target;
        EditorGUILayout.HelpBox(p.Status, MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (new EditorGUI.DisabledScope(p.Busy)) if (GUILayout.Button("밀기 시작")) p.BeginPush();
            if (GUILayout.Button("판 원위치로 복귀")) p.ReturnHome();
            if (GUILayout.Button("정지")) p.Stop();
        }
        if (Application.isPlaying) Repaint();
    }
}

public static class ConfigureParcelPusher
{
    const string Report = "Temp/ParcelPusherSetup.json";
    [MenuItem("Tools/Bonguri/Configure Parcel Pusher")]
    public static void Setup()
    {
        try
        {
            var plate = GameObject.Find("SeparatorB");
            if (plate == null) throw new Exception("SeparatorB missing");
            var scene = plate.scene;
            string backup = "Backups/Pusher/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/bongyuri.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); EditorSceneManager.SaveScene(scene, backup, true);
            var boxes = UnityEngine.Object.FindObjectsByType<Transform>().Where(t => t.Find("CardboardBody") != null).ToArray();
            var parcel = boxes.OrderBy(t => Vector3.Distance(t.position, plate.transform.position)).First().GetComponent<BoxCollider>();
            var belt = GameObject.Find("ConveyorBeltStraightMixed.029 (2)");
            if (belt == null) throw new Exception("Receiver belt missing");
            var renderer = belt.GetComponentsInChildren<MeshRenderer>(true).First(r => r.name.Contains("LOD0"));
            var conveyor = belt.GetComponent<LinearConveyor>();
            if (conveyor == null) conveyor = Undo.AddComponent<LinearConveyor>(belt);
            var so = new SerializedObject(conveyor);
            so.FindProperty("motor.startRunning").boolValue = false;
            so.FindProperty("motor.baseSpeed").floatValue = .12f;
            var along = Mathf.Abs(Vector3.Dot(belt.transform.forward, Vector3.back)) > .9f ? belt.transform.forward : belt.transform.right;
            so.FindProperty("movementAxis").enumValueIndex = Mathf.Abs(Vector3.Dot(belt.transform.forward, Vector3.back)) > .9f ? (int)ConveyorAxis.Forward : (int)ConveyorAxis.Right;
            so.FindProperty("motor.direction").intValue = Vector3.Dot(along, Vector3.back) > 0 ? -1 : 1;
            so.ApplyModifiedProperties();
            var rb = plate.GetComponent<Rigidbody>(); if (rb == null) rb = Undo.AddComponent<Rigidbody>(plate);
            rb.isKinematic = true; rb.useGravity = false;
            var push = plate.GetComponent<ParcelPusher>(); if (push == null) push = Undo.AddComponent<ParcelPusher>(plate);
            push.parcel = parcel; push.receiver = conveyor; push.direction = Vector3.back;
            push.receiverCenter = renderer.bounds.center; push.receiverSize = renderer.bounds.size;
            push.maxTravel = plate.transform.position.z - (renderer.bounds.max.z - .16f);
            EditorUtility.SetDirty(push); EditorUtility.SetDirty(conveyor); EditorUtility.SetDirty(rb);
            PrefabUtility.RecordPrefabInstancePropertyModifications(conveyor);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = plate;
            File.WriteAllText(Report, "Configured " + plate.name + " parcel=" + parcel.name + " receiver=" + belt.name + " travel=" + push.maxTravel + " bounds=" + renderer.bounds);
        }
        catch (Exception e) { File.WriteAllText(Report, "ERROR: " + e); Debug.LogException(e); }
    }
}
