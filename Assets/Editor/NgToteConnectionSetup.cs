using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NgToteConnectionSetup
{
    public static void Run()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/bongyuri.unity");
            var objects = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var bin = objects.Single(t => t.name == "RejectCollectionBox");
            var cell = objects.Select(t => t.GetComponent<CellController>()).Single(c => c != null);
            var pusher = objects.Single(t => t.name == "SeparatorC").GetComponent<BoxPusher>();
            if (pusher == null) throw new Exception("SeparatorC BoxPusher missing");
            var floor = bin.GetComponentsInChildren<BoxCollider>().Where(c => !c.isTrigger).OrderBy(c => c.bounds.center.y).First();
            var tote = bin.GetComponent<NgTote>();
            if (tote == null) tote = bin.gameObject.AddComponent<NgTote>();
            var ts = new SerializedObject(tote);
            ts.FindProperty("cell").objectReferenceValue = cell;
            ts.FindProperty("floorTop").floatValue = floor.bounds.max.y - bin.position.y + .002f;
            ts.ApplyModifiedPropertiesWithoutUndo();
            var ps = new SerializedObject(pusher);
            if (ps.FindProperty("target").intValue != (int)Destination.NG) throw new Exception("SeparatorC is not NG");
            ps.FindProperty("toLine").objectReferenceValue = null;
            ps.FindProperty("toTote").objectReferenceValue = tote;
            ps.FindProperty("divertPoint").objectReferenceValue = objects.Single(t => t.name == "SeparatorC_Divert");
            ps.FindProperty("pusherHead").objectReferenceValue = pusher.transform;
            ps.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pusher);
            EditorUtility.SetDirty(tote);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/NgToteConnection.txt", "PASS: SeparatorC -> RejectCollectionBox.NgTote -> CellController; divert/head connected; floorTop=" + ts.FindProperty("floorTop").floatValue + "; existing Add duplicate guard and Finish IsFinished guard verified in source. Play motion not tested.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
