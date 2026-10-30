using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(CorrectionSequence))]
public class CorrectionSequenceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var arm = (CorrectionSequence)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(arm.Status, MessageType.Info);
        EditorGUILayout.HelpBox("Target Box에 집을 박스의 Box Collider를 지정하세요. 경로상의 장애물을 자동으로 피하는 기능은 포함되어 있지 않습니다.", MessageType.None);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (new EditorGUI.DisabledScope(arm.Moving))
            {
                if (GUILayout.Button("1. 박스 위로 이동")) arm.ApproachBox();
                if (GUILayout.Button("2. 잡는 위치로 내려가기")) arm.DescendToBox();
                if (GUILayout.Button("3. 위로 들어 올리기")) arm.Lift();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("그리퍼 열기")) arm.OpenGripper();
                if (GUILayout.Button("그리퍼 닫기")) arm.CloseGripper();
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("정지")) arm.Stop();
        }
        if (GUILayout.Button("IK 계산만 검증 (팔 이동 없음)")) Debug.Log(CorrectionSequenceSetup.Validate(arm), arm);
        if (Application.isPlaying) Repaint();
    }
}

public static class CorrectionSequenceSetup
{
    const string Report = "Temp/CorrectionSequenceSetup.json";
    [MenuItem("Tools/Bonguri/Configure Parcel Arm IK")]
    public static void Setup()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/bongyuri.unity") return;
        SessionState.SetBool(Report, true);
        try
        {
            string backup = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments) + "/UnityBackups/bonguriProject/SceneBackups/ArmIK/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/bongyuri.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new Exception("Scene backup failed");
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var boxes = all.Where(t => t.Find("CardboardBody") != null && t.GetComponent<BoxCollider>() != null).Select(t => t.GetComponent<BoxCollider>()).ToArray();
            var roots = all.Where(t => t.name.StartsWith("fr5_with_gripper", StringComparison.Ordinal) && t.GetComponentsInChildren<Transform>(true).Any(c => c.name == "tcp")).ToArray();
            if (roots.Length == 0 || boxes.Length == 0) throw new Exception("Robot or parcel missing from active scene");
            var results = new string[roots.Length];
            CorrectionSequence selected = null;
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i]; var arm = root.GetComponent<CorrectionSequence>();
                if (arm == null) arm = Undo.AddComponent<CorrectionSequence>(root.gameObject);
                Undo.RecordObject(arm, "Configure arm IK");
                arm.tcp = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "tcp");
                arm.targetBox = boxes.OrderBy(b => (b.bounds.center - arm.tcp.position).sqrMagnitude).First();
                arm.gripper = root.GetComponent<GripperController>();
                if (arm.gripper == null) arm.gripper = Undo.AddComponent<GripperController>(root.gameObject);
                EditorUtility.SetDirty(arm); PrefabUtility.RecordPrefabInstancePropertyModifications(arm);
                results[i] = Validate(arm);
                if (selected == null || root.name.Contains("first")) selected = arm;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed");
            Selection.activeGameObject = selected.gameObject;
            File.WriteAllText(Report, JsonUtility.ToJson(new Result { robots = roots.Length, backup = backup, validations = results }, true));
            Debug.Log("Parcel Arm IK configured. Select the robot, enter Play, then use the IK inspector buttons.");
        }
        catch (Exception e) { File.WriteAllText(Report, "ERROR: " + e); Debug.LogException(e); }
    }
    public static string Validate(CorrectionSequence arm)
    {
        var model = new ArmKinematics(arm.transform, arm.tcp);
        var seed = model.CurrentAngles();
        model.Forward(seed, out var p, out var r);
        float fk = Vector3.Distance(p, arm.tcp.position);
        if (fk > .0001f || Quaternion.Angle(r, arm.tcp.rotation) > .1f) throw new Exception("Forward kinematics mismatch");
        var known = (float[])seed.Clone();
        for (int i = 0; i < known.Length; i++)
        { var d = model.Joints[i].xDrive; known[i] = Mathf.Clamp(known[i] + (i % 2 == 0 ? 12 : -9), d.lowerLimit, d.upperLimit); }
        model.Forward(known, out var testP, out var testR);
        bool reachable = model.Solve(testP, testR, seed, out var answer, out float error, out float angle);
        if (!reachable) throw new Exception("Known reachable IK pose failed: " + error + "m / " + angle + "deg");
        bool impossible = model.Solve(p + Vector3.one * 1000, r, seed, out var rejected, out _, out _);
        if (impossible) throw new Exception("Unreachable goal incorrectly accepted");
        arm.GetBoxGoal(arm.approachHeight, out var goalP, out var goalR);
        bool boxReachable = model.Solve(goalP, goalR, seed, out var boxAnswer, out float boxError, out float boxAngle);
        return $"{arm.name}: six joints; FK {fk * 1000:F3}mm; known-pose IK {error * 1000:F2}mm/{angle:F2}deg; unreachable rejected; box={arm.targetBox.name}; approach reachable={boxReachable} ({boxError * 1000:F1}mm/{boxAngle:F1}deg); scale={arm.transform.lossyScale}";
    }
    [Serializable] class Result { public int robots; public string backup; public string[] validations; }
}
