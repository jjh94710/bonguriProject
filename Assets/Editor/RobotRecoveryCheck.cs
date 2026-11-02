using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RobotRecoveryCheck
{
    const string Key = "Bonguri.RobotRecoveryCheck";
    const string RequestPath = "Temp/RunRobotRecoveryCheck.request";

    static CorrectionSequence arm;
    static double startedAt;
    static int phase;

    static RobotRecoveryCheck()
    {
        // A crashed or aborted test must never hijack the next manual Play.
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
            SessionState.SetBool(Key, false);

        EditorApplication.update += PollForRequest;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/Bonguri/Run Robot Recovery Check")]
    public static void Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var sequences = UnityEngine.Object.FindObjectsByType<CorrectionSequence>();
        File.WriteAllText("Temp/RobotRecoveryReport.txt", string.Join("\n", sequences.Select(a =>
            a.name + " pos=" + a.transform.position +
            " box=" + (a.targetBox == null ? "missing" : a.targetBox.bounds.center.ToString()) +
            " scale=" + a.transform.lossyScale)));

        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    static void PollForRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath))
            return;

        File.Delete(RequestPath);
        Run();
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false))
            return;

        phase = 0;
        arm = null;
        startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.update -= Tick;
            return;
        }

        EditorApplication.isPaused = false;
        Time.timeScale = 1;
        Application.runInBackground = true;
        EditorApplication.QueuePlayerLoopUpdate();

        try
        {
            if (phase == 0 && Time.time > .6f)
            {
                arm = UnityEngine.Object.FindObjectsByType<CorrectionSequence>()
                    .Single(a => a.name == "fr5_with_gripper_first");
                Selection.activeGameObject = arm.gameObject;
                arm.faceToRaise = CorrectionSequence.ParcelFace.Bottom;
                arm.RaiseSelectedFaceAndPlace();
                phase = 1;
            }

            if (phase != 1 || arm == null)
                return;

            File.WriteAllText("Temp/RobotRecoveryProgress.txt",
                "time=" + Time.time + " phase=" + arm.CycleStep + " status=" + arm.Status +
                " angle=" + Vector3.Angle(arm.SelectedFaceNormal, Vector3.up));

            if ((!arm.SequenceRunning && !arm.Moving) || EditorApplication.timeSinceStartup - startedAt > 210)
                Finish();
        }
        catch (Exception e)
        {
            File.AppendAllText("Temp/RobotRecoveryReport.txt", "\nERROR " + e);
            Finish();
        }
    }

    static void Finish()
    {
        if (arm != null)
            File.AppendAllText("Temp/RobotRecoveryReport.txt",
                "\nEND " + arm.Status + " angle=" + Vector3.Angle(arm.SelectedFaceNormal, Vector3.up));

        EditorApplication.update -= Tick;
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
    }
}
