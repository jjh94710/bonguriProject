using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;


public static class ArrangeParcelIKStation
{
    const string Report = "Temp/ParcelIKStation-v6.json";
[MenuItem("Tools/Bonguri/Arrange Robot Pick Station")]
    public static void Arrange()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(Report, true);
        try
        {
            var scene = SceneManager.GetActiveScene();
            string backup = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) + "/UnityBackups/bonguriProject/SceneBackups/PickStation/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/bongyuri.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new Exception("Backup failed");
            var arm = UnityEngine.Object.FindObjectsByType<CorrectionSequence>().First(a => a.name.Contains("first"));
            var box = arm.targetBox;
            if (box == null) throw new Exception("Target box missing");
            var baseBody = arm.GetComponentsInChildren<ArticulationBody>().First(b => b.isRoot);
            foreach (var joint in arm.GetComponentsInChildren<ArticulationBody>())
            {
                if (joint.isRoot) continue;
                var sourceJoint = PrefabUtility.GetCorrespondingObjectFromSource(joint);
                if (sourceJoint == null) continue;
                Undo.RecordObject(joint, "Restore URDF joint anchors");
                Undo.RecordObject(joint.transform, "Restore URDF joint frame");
                joint.matchAnchors = false;
                joint.transform.localPosition = sourceJoint.transform.localPosition;
                joint.transform.localRotation = sourceJoint.transform.localRotation;
                joint.transform.localScale = sourceJoint.transform.localScale;
                joint.anchorPosition = sourceJoint.anchorPosition;
                joint.anchorRotation = sourceJoint.anchorRotation;
                joint.parentAnchorPosition = sourceJoint.parentAnchorPosition;
                joint.parentAnchorRotation = sourceJoint.parentAnchorRotation;
                PrefabUtility.RecordPrefabInstancePropertyModifications(joint);
                PrefabUtility.RecordPrefabInstancePropertyModifications(joint.transform);
            }
            Vector3 outward = Vector3.ProjectOnPlane(baseBody.transform.position - box.bounds.center, Vector3.up).normalized;
            if (outward.sqrMagnitude < .9f) outward = -Vector3.right;
            bool found = false;
            foreach (float distance in new[] { .38f, .46f, .54f })
            {
                Vector3 basePosition = box.bounds.center + outward * distance;
                basePosition.y = box.bounds.min.y - .02f;
                Undo.RecordObject(arm.transform, "Arrange robot beside belt");
                arm.transform.position += basePosition - baseBody.transform.position;
                var model = new ArmKinematics(arm.transform, arm.tcp);
                arm.GetBoxGoal(.15f, out var goal, out var rotation);
                if (!model.Solve(goal, rotation, model.CurrentAngles(), out var goalQ, out _, out _, 5f, candidate => model.IsBoxClear(candidate, box, .015f))) continue;
                foreach (float height in new[] { .06f, .04f, .09f })
                {
                    Vector3 start = goal + outward * .04f + Vector3.up * height;
                    if (!model.Solve(start, rotation, goalQ, out var q, out _, out _, 5f, candidate => model.IsBoxClear(candidate, box, .015f))) continue;
                    if (!model.IsBoxClear(q, box, .015f)) continue;
                    foreach (var joint in model.Joints) { Undo.RecordObject(joint, "Set initial arm pose"); Undo.RecordObject(joint.transform, "Set initial arm pose"); }
                    model.ApplyEditorPose(q);
                    arm.SetInitialJointAngles(q);
                    foreach (var joint in model.Joints) { EditorUtility.SetDirty(joint); EditorUtility.SetDirty(joint.transform); PrefabUtility.RecordPrefabInstancePropertyModifications(joint); PrefabUtility.RecordPrefabInstancePropertyModifications(joint.transform); }
                    found = true; break;
                }
                if (found) break;
            }
            if (!found) throw new Exception("Could not arrange collision-free starting pose");
            arm.approachHeight = .15f; arm.travelClearance = .08f; arm.metresPerSecond = .05f;
            arm.angleTolerance = 6;
            arm.degreesPerSecond = 15; arm.gripper.closeAmount = 0;
            PrefabUtility.RecordPrefabInstancePropertyModifications(arm.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(arm);
            PrefabUtility.RecordPrefabInstancePropertyModifications(arm.gripper);
            EditorUtility.SetDirty(arm); EditorUtility.SetDirty(arm.gripper);
            baseBody.immovable = true;
            var pedestal = GameObject.Find("IK_TestPedestal");
            if (pedestal == null) { pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube); pedestal.name = "IK_TestPedestal"; Undo.RegisterCreatedObjectUndo(pedestal, "Create pick station pedestal"); }
            Vector3 basePoint = baseBody.transform.position;
            pedestal.transform.position = new Vector3(basePoint.x, basePoint.y * .5f, basePoint.z);
            pedestal.transform.localScale = new Vector3(.18f, Mathf.Max(.1f, basePoint.y), .18f);
            var source = arm.transform.parent == null ? null : arm.transform.parent.GetComponent<MeshRenderer>();
            if (source != null) pedestal.GetComponent<MeshRenderer>().sharedMaterial = source.sharedMaterial;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed");
            Selection.activeGameObject = arm.gameObject;
            SceneView.lastActiveSceneView?.Frame(new Bounds(box.bounds.center + Vector3.up * .2f, Vector3.one * 1.5f), false);
            File.WriteAllText(Report, JsonUtility.ToJson(new Result { backup = backup, root = arm.transform.position, tcp = arm.tcp.position, box = box.bounds.center }, true));


        }
        catch (Exception e) { File.WriteAllText(Report, "ERROR: " + e); Debug.LogException(e); }
    }
    [Serializable] class Result { public string backup; public Vector3 root, tcp, box; }
}
