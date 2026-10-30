using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad]
public static class ParcelPickCheck
{
    const string Report = "Temp/ParcelPickCheck.json", Key = "Bonguri.PickCheck";
    static bool fullCycle; static Quaternion turnedRotation; static Vector3 boxStart; static CorrectionSequence arm; static int stage; static float since, boxHeight; static double wall;
    static ParcelPickCheck()
    {
        EditorApplication.update += CheckRequest;
        EditorApplication.playModeStateChanged += s => {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { fullCycle = SessionState.GetBool("Bonguri.FullPickCycle", false); SessionState.SetBool("Bonguri.FullPickCycle", false); stage = 0; wall = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }
        };

    }
    static void CheckRequest()
    {
        const string request = "Temp/RequestParcelTurnPlace";
        if (!File.Exists(request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Move(request, request + "." + DateTime.Now.Ticks);
        EditorApplication.update -= CheckRequest; SessionState.SetBool("Bonguri.FullPickCycle", true); Begin();
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
            if (arm != null) File.WriteAllText("Temp/TurnPlaceProgress.txt", "stage=" + stage + " sim=" + Time.time + " status=" + arm.Status + " boxrot=" + arm.targetBox.transform.eulerAngles + " box=" + arm.targetBox.bounds.center + " tcp=" + arm.tcp.position);
            if (EditorApplication.timeSinceStartup - wall > 420) { Finish(false, "Test timeout"); return; }
            if (stage == 0)
            { arm = UnityEngine.Object.FindObjectsByType<CorrectionSequence>().First(a => a.name.Contains("first")); arm.sideGrasp = true; boxStart = arm.targetBox.bounds.center; arm.OpenGripper(); arm.ApproachBox(); stage = 1; }
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
                arm.gripper.closeAmount = arm.parcelGripCloseAmount * Mathf.Clamp01((Time.time - since) / 3f);
                if (Time.time - since > 5) { boxHeight = arm.targetBox.bounds.center.y; arm.Lift(); stage = 4; }
            }
            else if (stage == 4 && !arm.Moving)
            {
                if (!fullCycle) { Finish(arm.targetBox.bounds.center.y - boxHeight > .03f, "Box lift=" + (arm.targetBox.bounds.center.y - boxHeight).ToString("F4") + "m; " + arm.Status); return; }
                if (arm.targetBox.bounds.center.y - boxHeight < .03f) { Finish(false, "Failed physical lift"); return; }
                arm.TurnHeldParcel90(); stage = 5;
            }
            else if (stage == 5 && !arm.Moving && !arm.SequenceRunning)
            {
                if (!arm.Status.Contains("선택 회전 완료")) { Finish(false, arm.Status); return; }
                turnedRotation = arm.ExpectedTurnedBox;
                arm.PlaceTurnedParcel(); stage = 6;
            }
            else if (stage == 6 && !arm.Moving && !arm.SequenceRunning)
            {
                if (!arm.Status.Contains("벨트 놓기 완료")) { Finish(false, arm.Status); return; }
                since = Time.time; stage = 7;
            }
            else if (stage == 7 && Time.time - since > 2)
            {
                float angle = Quaternion.Angle(turnedRotation, arm.targetBox.transform.rotation);
                var rb = arm.targetBox.attachedRigidbody;
                Finish(angle < 10 && rb != null && rb.linearVelocity.magnitude < .03f,
                    "Placed angle drift=" + angle + " velocity=" + (rb == null ? -1 : rb.linearVelocity.magnitude) + "; " + arm.Status);
            }
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
