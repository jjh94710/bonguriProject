using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad]
public static class LowerPickStation
{
    static LowerPickStation() { EditorApplication.delayCall += Apply; }
    static void Apply()
    {
        const string report = "Temp/LowerPickStation.json";
        if (File.Exists(report)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Apply; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var arm = UnityEngine.Object.FindObjectsByType<ParcelArmIK>().First(a => a.name.Contains("first"));
        var scene = arm.gameObject.scene;
        string backup = "Backups/LowerStation/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/bongyuri.unity";
        Directory.CreateDirectory(Path.GetDirectoryName(backup)); EditorSceneManager.SaveScene(scene, backup, true);
        Undo.RecordObject(arm.transform, "Lower pick station"); arm.transform.position -= Vector3.up * .12f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(arm.transform);
        var pedestal = GameObject.Find("IK_TestPedestal");
        if (pedestal != null) { var scale = pedestal.transform.localScale; scale.y -= .12f; pedestal.transform.localScale = scale; pedestal.transform.position -= Vector3.up * .06f; }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        SetRobotReadyPose.Apply();
        File.WriteAllText(report, "Lowered robot mount 12cm. Backup: " + backup);
        EditorApplication.delayCall += Test;
    }
    static void Test()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Test; return; }
        ParcelArmIKPlayTest.Run();
    }
}
