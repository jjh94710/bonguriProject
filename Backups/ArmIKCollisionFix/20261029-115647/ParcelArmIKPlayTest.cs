using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ParcelArmIKPlayTest
{
    const string Report = "Temp/ParcelArmIKPlayTest-v2.json";
    const string Key = "Bonguri.IK.PlayTest.v2";
    static double start;
    static ParcelArmIK arm;
    static bool requested;
    static float initialError;
    static ArmKinematics model;
    static ParcelArmIKPlayTest()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Key, false);
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { start = EditorApplication.timeSinceStartup; requested = false; EditorApplication.update += Tick; }
        };
    }
    [MenuItem("Tools/Bonguri/Run IK Approach Play Test")]
    public static void Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        double elapsed = EditorApplication.timeSinceStartup - start;
        if (elapsed < 2) return;
        try
        {
            if (!requested)
            {
                arm = UnityEngine.Object.FindObjectsByType<ParcelArmIK>().First(a => a.name.Contains("first"));
                model = new ArmKinematics(arm.transform, arm.tcp);
                model.Forward(model.CurrentAngles(), out var p, out var r);
                initialError = Vector3.Distance(p, arm.tcp.position);
                if (initialError > .002f) throw new Exception("Runtime forward model mismatch: " + initialError);
                arm.ApproachBox(); requested = true;
                if (!arm.Moving) Finish(false, arm.Status);
            }
            else if (arm == null) Finish(false, "Robot missing");
            else if (!arm.Moving) Finish(arm.PositionError < arm.positionTolerance && Quaternion.Angle(arm.tcp.rotation, arm.GoalRotation) < arm.angleTolerance, arm.Status);
            else if (elapsed > 150) { Finish(false, "Editor runtime test timeout"); arm.Stop(); }
            if (requested && arm != null && arm.Moving) WriteProgress();
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    static void Finish(bool passed, string message)
    {
        EditorApplication.update -= Tick;
        SessionState.SetBool(Key, false);
        File.WriteAllText(Report, JsonUtility.ToJson(new Result { passed = passed, message = message, initialFKError = initialError,
            positionError = arm == null ? -1 : arm.PositionError,
            angleError = arm == null ? -1 : Quaternion.Angle(arm.tcp.rotation, arm.GoalRotation),
            goal = arm == null ? Vector3.zero : arm.GoalPosition, actual = arm == null ? Vector3.zero : arm.tcp.position,
            simulationTime = Time.time, joints = model == null ? "" : string.Join("; ", model.Joints.Select(j => j.name + ": actual=" + (j.jointPosition[0] * Mathf.Rad2Deg).ToString("F1") + " drive=" + j.xDrive.target.ToString("F1"))) }, true));
        EditorApplication.isPlaying = false;
    }
    static void WriteProgress()
    {
        model.Forward(model.CurrentAngles(), out var p, out var r);
        File.WriteAllText("Temp/ParcelArmIKProgress.txt", "sim=" + Time.time + " pos=" + arm.PositionError + " angle=" + Quaternion.Angle(arm.tcp.rotation, arm.GoalRotation) + " FK drift=" + Vector3.Distance(p, arm.tcp.position) + "\n" + string.Join("; ", model.Joints.Select(j => j.name + ": actual=" + (j.jointPosition[0] * Mathf.Rad2Deg).ToString("F1") + " drive=" + j.xDrive.target.ToString("F1"))));
    }
    [Serializable] class Result { public bool passed; public string message, joints; public float initialFKError, positionError, angleError, simulationTime; public Vector3 goal, actual; }
}
