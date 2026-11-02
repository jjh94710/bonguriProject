using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>검증 씬에서 CellController → ONNX → BoxInfo 경로와 오류 처리를 기록한다.</summary>
public sealed class InferenceIntegrationCheck : MonoBehaviour
{
    [SerializeField] FaceJudgeAgent agent;
    [SerializeField] LabelPresenceDetector detector;
    [SerializeField] FaceCameraRig rig;
    [SerializeField] CellController cell;
    readonly List<double> milliseconds = new List<double>();
    int observed;
    bool done;
    int rotationTests, rotationPassed;

    public void VerifyRotation(BoxInfo box, BoxFace face)
    {
        if (rotationTests >= 30 || face == BoxFace.Top || face == BoxFace.Bottom || face == BoxFace.None) return;
        var originalRotation = box.transform.rotation;
        var bodies = box.GetComponentsInChildren<Rigidbody>(true);
        try
        {
            box.transform.rotation = Quaternion.FromToRotation(FaceCameraRig.OutwardOf(face), Vector3.up) * originalRotation;
            foreach (var body in bodies) { body.position = body.transform.position; body.rotation = body.transform.rotation; }
            rotationTests++;
            if (cell.TryJudgeFace(box) && box.judgedFace == BoxFace.Top) rotationPassed++;
        }
        finally
        {
            box.transform.rotation = originalRotation;
            foreach (var body in bodies) { body.position = body.transform.position; body.rotation = body.transform.rotation; }
            if (!cell.TryJudgeFace(box)) throw new InvalidOperationException("회전 검증 후 원래 상자 판정을 복원하지 못했습니다.");
        }
    }

    [Serializable] class Report
    {
        public string unityVersion, model, backend = "Unity.InferenceEngine CPU", input = "image float32 [1,3,96,96] RGB 0..1 top-left", output = "label_score float32 [1,1]";
        public int completed, correct;
        public int rotationTests, rotationPassed;
        public int[] faceTotal, faceCorrect;
        public double coldMilliseconds, warmP50Milliseconds, warmP95Milliseconds, warmMaxMilliseconds;
        public bool missingModelRejected, invalidImageRejected, missingCameraRejected, disabledRejected,
            layersRestoredOnFailure, layersRestoredOnSuccess, reloadSucceeded, nullBoxRejected, passed;
    }

    void Update()
    {
        if (done || agent == null || agent.TrainingStats == null) return;
        if (agent.TrainingStats.Completed != observed)
        {
            observed = agent.TrainingStats.Completed;
            milliseconds.Add(detector.LastMilliseconds);
        }
        if (!agent.EvaluationFinished) return;
        done = true;
        var stats = agent.TrainingStats;
        var result = new Report { unityVersion = Application.unityVersion, model = detector.ModelName,
            rotationTests = rotationTests, rotationPassed = rotationPassed,
            completed = stats.Completed, correct = stats.Correct, faceTotal = stats.FaceTotal, faceCorrect = stats.FaceCorrect,
            coldMilliseconds = milliseconds.Count > 0 ? milliseconds[0] : 0, nullBoxRejected = !cell.TryJudgeFace(null) };
        var warm = milliseconds.Count > 1 ? milliseconds.GetRange(1, milliseconds.Count - 1) : new List<double>();
        warm.Sort();
        if (warm.Count > 0)
        {
            result.warmP50Milliseconds = warm[(warm.Count - 1) / 2];
            result.warmP95Milliseconds = warm[(int)Math.Ceiling(warm.Count * .95) - 1];
            result.warmMaxMilliseconds = warm[warm.Count - 1];
        }
        var probe = new GameObject("InferenceValidationProbe");
        var child = new GameObject("MixedLayerChild");
        child.transform.SetParent(probe.transform);
        child.layer = 2;
        var box = probe.AddComponent<BoxInfo>();
        var empty = probe.AddComponent<LabelPresenceDetector>();
        result.missingModelRejected = !empty.ValidateConfiguration(out _);
        var tiny = new Texture2D(2, 2);
        try { detector.Judge(new[] { tiny, tiny, tiny, tiny, tiny }); }
        catch (ArgumentException) { result.invalidImageRejected = true; }
        finally { Destroy(tiny); }
        var camera = rig.FaceCameras[4];
        try { rig.FaceCameras[4] = null; result.missingCameraRejected = !detector.TryJudgeBox(box, rig, out _); }
        finally { rig.FaceCameras[4] = camera; }
        try
        {
            detector.enabled = false;
            result.disabledRejected = !detector.TryJudgeBox(box, rig, out _);
            result.layersRestoredOnFailure = probe.layer == 0 && child.layer == 2;
        }
        finally { detector.enabled = true; }
        result.reloadSucceeded = detector.ValidateConfiguration(out _);
        result.layersRestoredOnSuccess = detector.TryJudgeBox(box, rig, out _) && probe.layer == 0 && child.layer == 2;
        Destroy(probe);
        result.passed = result.completed == 360 && result.correct == 360 && milliseconds.Count == 360 && rotationTests == 30 && rotationPassed == 30 &&
            result.missingModelRejected && result.invalidImageRejected && result.missingCameraRejected && result.disabledRejected &&
            result.layersRestoredOnFailure && result.layersRestoredOnSuccess && result.reloadSucceeded && result.nullBoxRejected;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results", "inference_integration"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
        File.WriteAllText(path, JsonUtility.ToJson(result, true));
        Debug.Log($"[Inference Engine 연동 검증] {(result.passed ? "PASS" : "FAIL")} · {result.correct}/{result.completed} · " +
            $"5면 전체 중앙값 {result.warmP50Milliseconds:F2} ms / P95 {result.warmP95Milliseconds:F2} ms → {path}");
    }
}
