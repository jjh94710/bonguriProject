using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;


public static class SetRobotReadyPose
{
    const string Report = "Temp/RobotReadyPoseElbowUp.json";
    [MenuItem("Tools/Bonguri/Set Raised Ready Pose")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            var scene = SceneManager.GetActiveScene();
            var arm = UnityEngine.Object.FindObjectsByType<ParcelArmIK>().First(a => a.name.Contains("first"));
            string backup = "Backups/ReadyPose/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/bongyuri.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new Exception("Backup failed");
            var model = new ArmKinematics(arm.transform, arm.tcp);
            bool found = false;
            float[] angles = null;
            foreach (float height in new[] { .35f, .40f, .30f, .45f })
            {
                if (!arm.GetBoxGoal(height, out var position, out var rotation)) throw new Exception("Box target missing");
                if (model.Solve(position, rotation, model.CurrentAngles(), out angles, out _, out _, 5f,
                    q => model.IsElbowRaised(q) && model.IsBoxClear(q, arm.targetBox, .015f))) { found = true; break; }
            }
            if (!found) throw new Exception("No collision-free raised ready pose found");
            Undo.RecordObject(arm, "Set robot ready pose");
            foreach (var joint in model.Joints) { Undo.RecordObject(joint, "Set ready angle"); Undo.RecordObject(joint.transform, "Set ready angle"); joint.matchAnchors = false; }
            model.ApplyEditorPose(angles); arm.SetInitialJointAngles(angles);
            foreach (var joint in model.Joints)
            { EditorUtility.SetDirty(joint); EditorUtility.SetDirty(joint.transform); PrefabUtility.RecordPrefabInstancePropertyModifications(joint); PrefabUtility.RecordPrefabInstancePropertyModifications(joint.transform); }
            EditorUtility.SetDirty(arm); PrefabUtility.RecordPrefabInstancePropertyModifications(arm);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = arm.gameObject;
            SceneView.lastActiveSceneView?.Frame(new Bounds(arm.tcp.position - Vector3.up * .15f, Vector3.one * 1.2f), false);
            File.WriteAllText(Report, "Saved raised downward-facing ready pose: " + string.Join(", ", angles.Select(a => a.ToString("F2"))) + "\nBackup: " + backup);
        }
        catch (Exception e) { File.WriteAllText(Report, "ERROR: " + e); Debug.LogException(e); }
    }
}
