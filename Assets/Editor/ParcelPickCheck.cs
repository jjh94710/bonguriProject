using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad]
public static class ParcelPickCheck
{
    const string Report = "Temp/ParcelPickCheck.json", Key = "Bonguri.PickCheck";
    static CorrectionSequence arm; static int stage; static float since, boxHeight; static double wall;
    static ParcelPickCheck()
    {
        EditorApplication.playModeStateChanged += s => {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { stage = 0; wall = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }
        };

    }
    [MenuItem("Tools/Bonguri/Run Parcel Pick And Lift Test")]
    public static void Begin()
    {

        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Begin; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        if (Time.time < .5f) return;
        try
        {
            if (EditorApplication.timeSinceStartup - wall > 240) { Finish(false, "Test timeout"); return; }
            if (stage == 0)
            { arm = UnityEngine.Object.FindObjectsByType<CorrectionSequence>().First(a => a.name.Contains("first")); arm.OpenGripper(); arm.ApproachBox(); stage = 1; }
            else if (stage == 1 && !arm.Moving)
            {
                if (arm.PositionError > .01f) { Finish(false, "Approach: " + arm.Status); return; }
                arm.DescendToBox(); stage = 2;
            }
            else if (stage == 2 && !arm.Moving)
            {
                if (arm.PositionError > .01f) { Finish(false, "Descent: " + arm.Status); return; }
                since = Time.time; stage = 3;
            }
            else if (stage == 3)
            {
                arm.gripper.closeAmount = .025f * Mathf.Clamp01((Time.time - since) / 3f);
                if (Time.time - since > 5) { boxHeight = arm.targetBox.bounds.center.y; arm.Lift(); stage = 4; }
            }
            else if (stage == 4 && !arm.Moving)
            { Finish(arm.targetBox.bounds.center.y - boxHeight > .03f, "Box lift=" + (arm.targetBox.bounds.center.y - boxHeight).ToString("F4") + "m; " + arm.Status); }
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    static void Finish(bool success, string message)
    {
        EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
        File.WriteAllText(Report, "success=" + success + " stage=" + stage + " " + message);
        EditorApplication.isPlaying = false;
    }
}
