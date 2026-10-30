using System;
using System.IO;
using UnityEditor;
using UnityEngine;
[InitializeOnLoad]
public static class BoxPusherPlayTest
{
    const string Report = "Temp/RightPusherPlayTest.json", Key = "Bonguri.PusherTest.Right";
    static BoxPusher p; static int stage; static float started; static Vector3 home, delivered; static double wall;
    static BoxPusherPlayTest()
    {

        EditorApplication.update += () => {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating && File.Exists("Temp/RunRestoredPusher.request")) {
                string name = File.ReadAllText("Temp/RunRestoredPusher.request").Trim(); File.Delete("Temp/RunRestoredPusher.request");
                Selection.activeGameObject = GameObject.Find(name); Begin();
            }
        };
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { stage = 0; wall = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }
        };

    }
    [MenuItem("Tools/Bonguri/Run Box Pusher Play Test")]
    public static void Begin()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        Application.runInBackground = true; EditorApplication.QueuePlayerLoopUpdate();
        if (Time.time < .6f) return;
        if(p!=null) File.WriteAllText("Temp/RestoredPusherProgress.txt", "sim="+Time.time+" "+p.Status+" box="+p.parcel.bounds.center);
        try
        {
            if (EditorApplication.timeSinceStartup - wall > 240) { Finish(false, "Wall timeout"); return; }
            if (stage == 0)
            { p = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponent<BoxPusher>(); if (p == null) p = GameObject.Find("SeparatorA").GetComponent<BoxPusher>(); home = p.transform.position; p.BeginPush(); started = Time.time; stage = 1; }
            else if(stage == 1 && p.Busy && p.oppositeSeparator.activeSelf) Finish(false, "Opposite separator remained active");
            else if (stage == 1 && p.Completed)
            { delivered = p.parcel.bounds.center; started = Time.time; stage = 2; }
            else if (stage == 1 && !p.Busy) Finish(false, p.Status);
            else if (stage == 2 && Time.time - started > 1)
            { Finish(Vector3.Distance(home, p.transform.position) < .003f && (p.startReceiverAfterReturn ? p.receiver.IsRunning && Vector3.Dot(p.parcel.bounds.center - delivered, p.direction.normalized) > .015f : !p.receiver.IsRunning && p.ParcelInsideReceiver(false)), p.Status); }
            else if (stage == 1 && Time.time - started > 40) Finish(false, "Cycle timed out: " + p.Status);
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    static void Finish(bool passed, string message)
    {
        EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
        File.WriteAllText(Report, JsonUtility.ToJson(new Result { passed = passed, message = message, stage = stage, homeError = p == null ? -1 : Vector3.Distance(home, p.transform.position), parcel = p == null ? Vector3.zero : p.parcel.bounds.center, receiverRunning = p != null && p.receiver.IsRunning, beltTravel = p == null ? 0 : Vector3.Distance(delivered, p.parcel.bounds.center) }, true));
        EditorApplication.isPlaying = false;
    }
    [Serializable] class Result { public bool passed, receiverRunning; public string message; public int stage; public float homeError, beltTravel; public Vector3 parcel; }
}
