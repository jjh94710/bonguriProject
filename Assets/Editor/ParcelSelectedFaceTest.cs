using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ParcelSelectedFaceTest
{
    const string Key = "Bonguri.FaceTest.Active";
    static CorrectionSequence arm;
    static bool started;
    static double since;
    static double readyAt;
    static ParcelSelectedFaceTest()
    {
        readyAt = EditorApplication.timeSinceStartup + 2;
        if (EditorApplication.isPlaying && SessionState.GetBool(Key, false))
            EditorApplication.delayCall += () => {
                SessionState.SetBool(Key, false); EditorApplication.isPlaying = false;
                if (!File.Exists("Temp/RequestSelectedFaceTest")) File.WriteAllText("Temp/RequestSelectedFaceTest", "recover after reload");
            };
        EditorApplication.update += Request;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { arm = null; started = false; since = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }
        };
    }
    static void Request()
    {
        if (EditorApplication.timeSinceStartup < readyAt) return;
        const string stop = "Temp/StopSelectedFaceTest";
        if (File.Exists(stop))
        {
            File.Move(stop, stop + "." + DateTime.Now.Ticks);
            SessionState.SetBool(Key, false); SessionState.SetBool("Bonguri.FaceTest.Next", false); EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false; return;
        }
        if (SessionState.GetBool("Bonguri.FaceTest.Next", false) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            SessionState.SetBool("Bonguri.FaceTest.Next", false);
            int index = SessionState.GetInt("Bonguri.FaceTest.Index", 0) + 1;
            SessionState.SetInt("Bonguri.FaceTest.Index", index);
            if (index < 2) { SessionState.SetBool(Key, true); Next(); }
            else SessionState.SetBool(Key, false);
            return;
        }
        const string path = "Temp/RequestSelectedFaceTest";
        if (!File.Exists(path) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        int first = int.TryParse(File.ReadAllText(path).Trim(), out int requestedIndex) ? requestedIndex : 0;
        File.Move(path, path + "." + DateTime.Now.Ticks);
        if (first == 0) File.WriteAllText("Temp/SelectedFaceResults.txt", "");
        SessionState.SetBool("Bonguri.FaceTest.Next", false); SessionState.SetBool(Key, true); SessionState.SetInt("Bonguri.FaceTest.Index", first); Next();
    }
    static void Next()
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.delayCall += Next; return; }
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.time < .5f) return;
        try
        {
            if (!started)
            {
                Application.runInBackground = true;
                arm = UnityEngine.Object.FindObjectsByType<CorrectionSequence>().First(a => a.name.Contains("first"));
                arm.faceToRaise = (CorrectionSequence.ParcelFace)SessionState.GetInt("Bonguri.FaceTest.Index", 0);
                arm.RaiseSelectedFaceAndPlace(); started = true;
            }
            float angle = Vector3.Angle(arm.SelectedFaceNormal, Vector3.up);
            File.WriteAllText("Temp/SelectedFaceProgress.txt", arm.faceToRaise + " sim=" + Time.time + " angle=" + angle + " jawHeightMm=" + arm.JawHeightDifference()*1000 + " closingY=" + arm.JawClosingDirection().y + " busy=" + arm.SequenceRunning + " step=" + arm.CycleStep + " status=" + arm.Status);
            if (!arm.SequenceRunning && !arm.Moving)
                Finish(arm.Status.StartsWith("선택 면 위로 놓기 완료") && angle < 10, angle);
            else if (EditorApplication.timeSinceStartup - since > 240) { arm.Stop(); Finish(false, angle); }
        }
        catch (Exception e) { File.AppendAllText("Temp/SelectedFaceResults.txt", e + "\n"); Finish(false, 180); }
    }
    static void Finish(bool success, float angle)
    {
        EditorApplication.update -= Tick;
        File.AppendAllText("Temp/SelectedFaceResults.txt", (arm == null ? "missing" : arm.faceToRaise.ToString()) + " success=" + success + " angle=" + angle + " step=" + (arm == null ? "" : arm.CycleStep) + " status=" + (arm == null ? "" : arm.Status) + "\n");
        SessionState.SetBool("Bonguri.FaceTest.Next", true);
        EditorApplication.isPlaying = false;
    }
}
