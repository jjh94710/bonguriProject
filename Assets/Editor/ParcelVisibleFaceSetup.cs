using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static class ParcelVisibleFaceSetup
{
    const string Key = "Bonguri.VisibleFace.Run";
    static double readyAt, started;
    static CorrectionSequence arm;
    static bool requested;
    static int lastCompleted;
    static ParcelVisibleFaceSetup()
    {
        readyAt = EditorApplication.timeSinceStartup + 4;
        if (EditorApplication.isPlaying && SessionState.GetBool(Key, false))
            EditorApplication.delayCall += () => { SessionState.SetBool(Key, false); EditorApplication.isPlaying = false; if (!File.Exists("Temp/RequestVisibleParcelFace")) File.WriteAllText("Temp/RequestVisibleParcelFace", ((CorrectionSequence.ParcelFace)SessionState.GetInt("Bonguri.VisibleFace.RequestedFace", 1)).ToString()); };
        EditorApplication.update += ConfigureRequest;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { arm = null; requested = false; lastCompleted = 0; started = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; }
        };
    }
    static void ConfigureRequest()
    {
        const string path = "Temp/RequestVisibleParcelFace";
        if (!File.Exists(path) || EditorApplication.timeSinceStartup < readyAt || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string request = File.ReadAllText(path).Trim();
        File.Move(path, path + "." + DateTime.Now.Ticks);
        var robot = UnityEngine.Object.FindObjectsByType<CorrectionSequence>().First(a => a.name.Contains("first"));
        if (Enum.TryParse<CorrectionSequence.ParcelFace>(request, out var requestedFace))
        { robot.faceToRaise = requestedFace; robot.sideGrasp = true; }
        else SelectVisibleFace(robot);
        var scene = robot.gameObject.scene;
        string backup = "Backups/VisibleFace/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/bongyuri.unity";
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        EditorSceneManager.SaveScene(scene, backup, true);
        EditorUtility.SetDirty(robot); PrefabUtility.RecordPrefabInstancePropertyModifications(robot);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = robot.gameObject;
        SessionState.SetInt("Bonguri.VisibleFace.RequestedFace", (int)robot.faceToRaise);
        File.WriteAllText("Temp/BottomQuarterSteps.txt", "");
        SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
    }
    public static void SelectVisibleFace(CorrectionSequence robot)
    {
        if (robot.targetBox == null || SceneView.lastActiveSceneView == null) throw new InvalidOperationException("박스와 Scene 화면이 필요합니다.");
        Vector3 view = SceneView.lastActiveSceneView.camera.transform.position - robot.targetBox.bounds.center;
        view = Vector3.ProjectOnPlane(view, Vector3.up).normalized;
        robot.faceToRaise = Mathf.Abs(view.x) > Mathf.Abs(view.z)
            ? (view.x > 0 ? CorrectionSequence.ParcelFace.Right : CorrectionSequence.ParcelFace.Left)
            : (view.z > 0 ? CorrectionSequence.ParcelFace.Front : CorrectionSequence.ParcelFace.Back);
        robot.sideGrasp = true;
        File.WriteAllText("Temp/VisibleParcelFaceSelection.txt", "face=" + robot.faceToRaise + " cameraDirection=" + view);
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.time < .5f) return;
        try
        {
            if (!requested)
            {
                arm = UnityEngine.Object.FindObjectsByType<CorrectionSequence>().First(a => a.name.Contains("first"));
                Application.runInBackground = true; arm.RaiseSelectedFaceAndPlace(); requested = true;
            }
            float angle = Vector3.Angle(arm.SelectedFaceNormal, Vector3.up);
            File.WriteAllText("Temp/VisibleParcelFaceProgress.txt", "face=" + arm.faceToRaise + " sim=" + Time.time + " error=" + angle + " stage=" + arm.CurrentRotationStage + " completed=" + arm.CompletedQuarterTurns + " stageAngle=" + Vector3.Angle(arm.StageFaceNormal, Vector3.up) + " step=" + arm.CycleStep + " status=" + arm.Status);
            if (arm.CompletedQuarterTurns > lastCompleted)
            {
                lastCompleted = arm.CompletedQuarterTurns;
                File.AppendAllText("Temp/BottomQuarterSteps.txt", "placed=" + lastCompleted + " originalBottomAngle=" + angle + " stageAngle=" + Vector3.Angle(arm.StageFaceNormal, Vector3.up) + " position=" + arm.targetBox.bounds.center + "\n");
            }
            if (!arm.SequenceRunning && !arm.Moving)
            {
                var body = arm.targetBox.attachedRigidbody;
                bool passed = arm.Status.StartsWith("선택 면 위로 놓기 완료") && angle < 10
                    && (arm.faceToRaise != CorrectionSequence.ParcelFace.Bottom || arm.CompletedQuarterTurns == 2)
                    && body != null && body.linearVelocity.magnitude < .03f;
                Finish(passed, "face=" + arm.faceToRaise + " angle=" + angle + " completed=" + arm.CompletedQuarterTurns + " " + arm.Status);
            }
            else if (EditorApplication.timeSinceStartup - started > 240) { arm.Stop(); Finish(false, "timeout: " + arm.Status); }
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    static void Finish(bool passed, string message)
    {
        EditorApplication.update -= Tick; SessionState.SetBool(Key, false);
        File.WriteAllText("Temp/VisibleParcelFaceResult.txt", "passed=" + passed + " " + message);
        if (passed) EditorApplication.isPaused = true;
        else EditorApplication.isPlaying = false;
    }
}
