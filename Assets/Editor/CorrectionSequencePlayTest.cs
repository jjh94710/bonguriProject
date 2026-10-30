using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CorrectionSequencePlayTest
{
    const string Report = "Temp/CorrectionSequencePlayTest-v3.json";
    const string Key = "Bonguri.IK.PlayTest.v3";
    static double start;
    static CorrectionSequence arm;
    static bool requested;
    static float initialError;
    static ArmKinematics model;
    static Vector3 initialBoxPosition;
    static float maxBoxMovement;
    static int overlappingFrames;
    static float lastMotionTime, nextProgress;
    static Vector3 lastMotionPosition;
    static CorrectionSequencePlayTest()
    {
        EditorApplication.delayCall += RequestedTest;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(Key, false);
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { start = EditorApplication.timeSinceStartup; requested = false; EditorApplication.update += Tick; }
        };
    }
    static void RequestedTest()
    {
        const string request = "Temp/RequestParcelIKTest";
        if (!File.Exists(request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += RequestedTest; return; }
        if (EditorApplication.isPlaying)
        { EditorApplication.isPlaying = false; EditorApplication.delayCall += RequestedTest; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += RequestedTest; return; }
        File.Move(request, request + "." + DateTime.Now.ToString("HHmmss"));
        Run();
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
        if (elapsed < 2 || Time.time < .5f) return;
        try
        {
            if (!requested)
            {
                arm = UnityEngine.Object.FindObjectsByType<CorrectionSequence>().First(a => a.name.Contains("first"));
                model = new ArmKinematics(arm.transform, arm.tcp);
                maxBoxMovement = Mathf.Max(maxBoxMovement, Vector3.Distance(initialBoxPosition, arm.targetBox.transform.position));
        if (!model.IsBoxClear(model.CurrentAngles(), arm.targetBox, 0)) overlappingFrames++;
        model.Forward(model.CurrentAngles(), out var p, out var r);
                initialError = Vector3.Distance(p, arm.tcp.position);
                if (initialError > .002f) throw new Exception("Runtime forward model mismatch: " + initialError);
                initialBoxPosition = arm.targetBox.transform.position; maxBoxMovement = 0; overlappingFrames = 0;
                lastMotionTime = Time.time; lastMotionPosition = arm.tcp.position; nextProgress = 0;
                arm.ApproachBox(); requested = true;
                if (!arm.Moving) Finish(false, arm.Status);
            }
            else if (arm == null) Finish(false, "Robot missing");
            else if (!arm.Moving) Finish(maxBoxMovement < .005f && overlappingFrames == 0 && arm.PositionError < arm.positionTolerance && Quaternion.Angle(arm.tcp.rotation, arm.GoalRotation) < arm.angleTolerance, arm.Status);
            else if (Vector3.Distance(arm.tcp.position, lastMotionPosition) > .001f) { lastMotionPosition = arm.tcp.position; lastMotionTime = Time.time; }
            else if (Time.time - lastMotionTime > 6) { Finish(false, "Arm stalled: " + Contacts()); arm.Stop(); }
            else if (elapsed > 240) { Finish(false, "Editor runtime test timeout"); arm.Stop(); }
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
            maxBoxMovement = maxBoxMovement, overlappingFrames = overlappingFrames, simulationTime = Time.time, contacts = Contacts(), joints = model == null ? "" : string.Join("; ", model.Joints.Select(j => j.name + ": actual=" + (j.jointPosition[0] * Mathf.Rad2Deg).ToString("F1") + " drive=" + j.xDrive.target.ToString("F1"))) }, true));
        EditorApplication.isPlaying = false;
    }
    static void WriteProgress()
    {
        if (Time.time < nextProgress) return;
        nextProgress = Time.time + 1;
        maxBoxMovement = Mathf.Max(maxBoxMovement, Vector3.Distance(initialBoxPosition, arm.targetBox.transform.position));
        if (!model.IsBoxClear(model.CurrentAngles(), arm.targetBox, 0)) overlappingFrames++;
        model.Forward(model.CurrentAngles(), out var p, out var r);
        File.WriteAllText("Temp/CorrectionSequenceProgress.txt", "sim=" + Time.time + " pos=" + arm.PositionError + " angle=" + Quaternion.Angle(arm.tcp.rotation, arm.GoalRotation) + " FK drift=" + Vector3.Distance(p, arm.tcp.position) + "\n" + string.Join("; ", model.Joints.Select(j => j.name + ": actual=" + (j.jointPosition[0] * Mathf.Rad2Deg).ToString("F1") + " drive=" + j.xDrive.target.ToString("F1"))) + "\n" + Contacts());
    }
    static string Contacts()
    {
        if (arm == null) return "";
        var result = new System.Collections.Generic.HashSet<string>();
        foreach (var c in arm.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            foreach (var other in Physics.OverlapBox(c.bounds.center, c.bounds.extents + Vector3.one * .002f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (other == c || Physics.GetIgnoreCollision(c, other)) continue;
                if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, other, other.transform.position, other.transform.rotation, out _, out float depth) && depth > .0001f)
                    result.Add(c.transform.parent.name + "/" + c.name + " vs " + other.transform.parent?.name + "/" + other.name + " " + depth.ToString("F4"));
            }
        }
        return string.Join("; ", result);
    }
    [Serializable] class Result { public bool passed; public string message, joints, contacts; public int overlappingFrames; public float maxBoxMovement, initialFKError, positionError, angleError, simulationTime; public Vector3 goal, actual; }
}

