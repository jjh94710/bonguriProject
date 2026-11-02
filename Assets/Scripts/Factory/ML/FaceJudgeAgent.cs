using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;

/// <summary>5면 영상만 관측하여 Top/Front/Back/Left/Right/Bottom을 고른다.
/// Bottom은 미검출(바닥 또는 미부착)이므로 이후 뒤집기/재검사 대상으로 취급한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Unity.MLAgents.Policies.BehaviorParameters))]
public class FaceJudgeAgent : Agent
{
    [SerializeField] FaceCameraRig rig;
    [Tooltip("학습 전용 씬에서만 켠다. 공정에서는 끄고 RequestFace를 호출한다.")]
    [SerializeField] bool trainingMode;
    [SerializeField] LabelDatasetGenerator.BoxEntry[] boxes;
    [SerializeField] string labelChildName = "Label";
    [Tooltip("학습 중 같은 씬의 기존 ParcelBox 상자와 BoxInfo 상자를 숨긴다. 학습 종료 시 복원한다.")]
    [SerializeField] bool hideSceneBoxesDuringTraining = true;
    readonly List<GameObject> hiddenSceneBoxes = new List<GameObject>();
    string currentBoxName = "--";
    [SerializeField, Range(0f, 0.5f)] float noLabelRatio = 0.1f;

    [Header("면 안의 송장 배치 변화 (학습 전용)")]
    [SerializeField] LabelPlacementRandomizer labelPlacement = new LabelPlacementRandomizer();

    [Header("미리보기 점검")]
    [Tooltip("켜면 한 상자를 실제 시간 기준으로 유지한 뒤 판정한다. 확인 후 끄면 원래 학습 속도로 돌아간다.")]
    [SerializeField] bool slowPreview;
    [SerializeField, Range(0.2f, 5f)] float previewHoldSeconds = 0.5f;
    double sampleCreatedAt;
    int sampleCreatedFrame;

    [Header("고정 ONNX 모델 평가")]
    [Tooltip("샘플은 자동 생성하지만 Python 학습 없이 연결된 ONNX만 평가한다.")]
    [SerializeField] bool evaluationOnly;
    [SerializeField] int evaluationSeed = 20261103;
    [Header("카메라별 송장 유무 지도학습")]
    [SerializeField] bool exportPresenceDataset;
    [SerializeField] string presenceDatasetFolder = "LabelPresenceDataset";
    [SerializeField] LabelPresenceDetector presenceDetector;
    string presenceDatasetPath;
    StreamWriter presenceManifest;
    LabelPresencePairCheck presencePairCheck;
    [SerializeField] CellController integrationCell;
    System.Random evaluationRandom;
    readonly int[] confusion = new int[36];
    public bool EvaluationFinished { get; private set; }

    [Header("정답 · 관측 · 보상 검증")]
    [SerializeField] bool auditDecisions = true;
    [SerializeField, Range(1, 100)] int auditSampleLimit = 12;
    FaceJudgeAudit audit;
    AuditedFaceCameraSensor[] auditSensors;
    GameObject currentLabel;
    bool auditPending, geometryKnown, auditLabelPresent;
    BoxFace geometryFace;
    int auditSample;

    [Header("학습 진행 표시")]
    [SerializeField, Min(1)] int targetBoxes = 5000;
    [SerializeField] bool showTrainingProgress = true;
    [SerializeField, Min(1)] int reportEveryBoxes = 250;
    public FaceJudgeTrainingStats TrainingStats { get; private set; }
    System.Diagnostics.Stopwatch trainingClock;
    int trainerJudgments;
    int nonTrainerJudgments;
    string reportPath;
    string progressText;
    float nextProgressRefresh;
    int lastSavedCount;
    bool reportFailed;
    bool milestoneReported;
    GUIStyle progressStyle;

    public BoxFace PredictedFace { get; private set; } = BoxFace.None;
    public bool IsPending { get; private set; }
    public event Action<BoxFace> FaceJudged;
    GameObject sample;
    readonly List<UnityEngine.Object> temporaryAssets = new List<UnityEngine.Object>();
    BoxFace expected;
    BoxInfo pendingBox;
    bool decisionQueued;

    public override void Initialize()
    {
        if (rig == null || !rig.IsBuilt)
            throw new InvalidOperationException("FaceJudgeAgent: 먼저 카메라 리그와 센서 5개를 설정하세요.");
        rig.EnsureCameraOrder();
        MaxStep = 0;
        if (trainingMode)
        {
            EvaluationFinished = false;
            Array.Clear(confusion, 0, confusion.Length);
            evaluationRandom = new System.Random(evaluationSeed);
            presencePairCheck = evaluationOnly && !exportPresenceDataset && presenceDetector != null ? new LabelPresencePairCheck() : null;
            if (exportPresenceDataset)
            {
                presenceDatasetPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", presenceDatasetFolder,
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)));
                Directory.CreateDirectory(Path.Combine(presenceDatasetPath, "images"));
                presenceManifest = new StreamWriter(Path.Combine(presenceDatasetPath, "labels.csv"), false, new UTF8Encoding(true));
                presenceManifest.WriteLine("file,sample,camera,has_label,true_face,box,split");
                Debug.Log("[송장 유무 데이터셋] " + presenceDatasetPath);
            }
            if (evaluationOnly && !exportPresenceDataset && presenceDetector == null)
            {
                var policy = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
                if (policy.BehaviorType != Unity.MLAgents.Policies.BehaviorType.InferenceOnly || policy.Model == null)
                    throw new InvalidOperationException("고정 모델 평가는 ONNX와 Inference Only 설정이 필요합니다.");
            }
            audit?.Dispose();
            audit = new FaceJudgeAudit();
            auditSensors = GetComponents<AuditedFaceCameraSensor>();
            Array.Sort(auditSensors, (a, b) => string.CompareOrdinal(a.SensorName, b.SensorName));
            if (auditSensors.Length != FaceCameraRig.FaceCount)
                Debug.LogWarning("[FaceJudge 검증] 실제 센서 기록을 사용하려면 Play 정지 후 면 판정 Agent 설정 메뉴를 다시 실행하세요.", this);
            HideSceneBoxes();
            TrainingStats = new FaceJudgeTrainingStats();
            trainingClock = System.Diagnostics.Stopwatch.StartNew();
            trainerJudgments = 0;
            nonTrainerJudgments = 0;
            reportPath = Path.Combine(Application.dataPath, "..", "results", "face_judge_monitor",
                DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N") + ".csv");
            lastSavedCount = 0;
            milestoneReported = false;
            reportFailed = false;
            progressText = null;
            nextProgressRefresh = 0f;
        }
    }

    public override void OnEpisodeBegin()
    {
        if (!trainingMode || EvaluationFinished) return;
        ClearSample();
        var available = new List<LabelDatasetGenerator.BoxEntry>();
        if (boxes != null)
            foreach (var entry in boxes)
                if (entry != null && entry.prefab != null && entry.weight > 0f) available.Add(entry);
        int sampleIndex = TrainingStats.Generated;
        var chosen = available.Count > 0 ? available[evaluationOnly ? (sampleIndex / 6) % available.Count : UnityEngine.Random.Range(0, available.Count)] : null;
        sample = chosen != null ? Instantiate(chosen.prefab) :
            SampleBox.Create(rig.BoxSize, SampleBox.ResolveLayer(SampleBox.DefaultLayerName), temporaryAssets);
        currentBoxName = chosen != null ? chosen.prefab.name : "SampleBox";
        sample.name = "TrainingBox_" + currentBoxName;
        // 비활성 프리팹을 등록해도 선택한 한 개는 반드시 보이게 한다.
        sample.SetActive(true);
        SampleBox.SetLayerRecursively(sample, SampleBox.ResolveLayer(SampleBox.DefaultLayerName));
        // 촬영 중 자세가 변하지 않도록 학습용 복사본만 고정한다.
        foreach (var body in sample.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
            body.interpolation = RigidbodyInterpolation.None;
        }
        // 촬영용 샘플은 물리 공정에 참여하지 않는다. 기존 상자/벨트를 밀지 않게 한다.
        foreach (var collider in sample.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var label = SampleBox.FindLabel(sample, labelChildName);
        currentLabel = label;
        Vector3 normal = chosen != null ? chosen.Normal : Vector3.forward;
        if (FaceJudgeAudit.TryGetPaperNormal(sample, label, out Vector3 paperNormal))
            normal = sample.transform.InverseTransformDirection(paperNormal).normalized;
        // 즉시 생성/이동/회전하는 촬영 샘플은 GPU 캐시 및 작은 메쉬 생략 대상에서 제외한다.
        foreach (var mesh in sample.GetComponentsInChildren<MeshRenderer>(true))
        {
            var captureProperties = new MaterialPropertyBlock();
            mesh.GetPropertyBlock(captureProperties);
            captureProperties.SetFloat("_FaceCaptureDynamic", 1f);
            mesh.SetPropertyBlock(captureProperties);
        }
        expected = (BoxFace)(evaluationOnly ? sampleIndex % 6 : UnityEngine.Random.Range(0, 6)); // 학습은 6개 클래스를 균등하게 뽑는다.
        Vector3 outward = FaceCameraRig.OutwardOf(expected);
        sample.transform.rotation = Quaternion.AngleAxis((evaluationOnly ? evaluationRandom.Next(0, 4) : UnityEngine.Random.Range(0, 4)) * 90f, outward)
            * Quaternion.FromToRotation(normal, outward);
        sample.transform.position = rig.BoxCenter;
        sample.transform.rotation = Quaternion.AngleAxis((evaluationOnly ? (float)(evaluationRandom.NextDouble() * 5 - 2.5) : UnityEngine.Random.Range(-2.5f, 2.5f)), outward) * sample.transform.rotation;
        LabelTextOcclusion.Prepare(label);
        if (label != null)
            new LabelPlacementRandomizer.Placement(sample, label, normal)
                .Apply(labelPlacement, () => evaluationOnly ? evaluationRandom.NextDouble() : UnityEngine.Random.value);
        if (label != null && (evaluationOnly ? expected == BoxFace.Bottom && ((sampleIndex / 18) % 2 == 1) : UnityEngine.Random.value < noLabelRatio))
        {
            label.SetActive(false);
            expected = BoxFace.Bottom;
        }
        if (TrainingStats.Generated < 3)
            Debug.Log($"[FacePose 생성] 원래법선={normal} 목표={expected} 실제={FaceJudgeAudit.GeometryFace(sample, label, out _)} root={sample.transform.rotation} label={label?.transform.localRotation}");
        foreach (var body in sample.GetComponentsInChildren<Rigidbody>(true))
        {
            body.position = body.transform.position;
            body.rotation = body.transform.rotation;
        }
        sampleCreatedAt = Time.realtimeSinceStartupAsDouble;
        sampleCreatedFrame = Time.frameCount;
        TrainingStats.RecordGenerated();
        PredictedFace = BoxFace.None;
        IsPending = true;
        decisionQueued = true;
    }

    /// <summary>상자가 정지한 뒤 호출. 결과는 FaceJudged 이벤트 및 box.judgedFace에 기록된다.</summary>
    public bool RequestFace(BoxInfo box)
    {
        if (trainingMode || IsPending || box == null || !isActiveAndEnabled) return false;
        var behavior = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
        if (presenceDetector == null && (behavior == null || behavior.Model == null)) return false;
        pendingBox = box;
        PredictedFace = BoxFace.None;
        IsPending = true;
        decisionQueued = true;
        return true;
    }

    void FixedUpdate()
    {
        if (!decisionQueued) return;
        if (trainingMode && Time.frameCount <= sampleCreatedFrame + 1) return;
        // timeScale을 올린 학습에서도 실제 0.5초 동안 같은 샘플을 볼 수 있다.
        if (trainingMode && slowPreview && Time.realtimeSinceStartupAsDouble - sampleCreatedAt < previewHoldSeconds) return;
        decisionQueued = false;
        if (exportPresenceDataset || presenceDetector != null)
        {
            var actual = FaceJudgeAudit.GeometryFace(sample, currentLabel, out bool known);
            if (trainingMode && (!known || actual != expected))
                throw new InvalidOperationException("송장 유무 표본의 실제 면과 정답이 일치하지 않습니다.");
            if (exportPresenceDataset)
            {
                var images = rig.Capture();
                try
                {
                    int index = TrainingStats.Generated - 1;
                    int block = (index / 18) % 10;
                    string split = block < 7 ? "train" : block == 7 ? "val" : "test";
                    for (int i = 0; i < 5; i++)
                    {
                        string file = $"images/{index:D6}_{i}.png";
                        File.WriteAllBytes(Path.Combine(presenceDatasetPath, file), images[i].EncodeToPNG());
                        presenceManifest.WriteLine($"{file},{index},{i},{(expected == FaceCameraRig.CapturedFaces[i] ? 1 : 0)},{(int)expected},{currentBoxName},{split}");
                    }
                }
                finally { FaceCameraRig.ReleaseShots(images); }
                TrainingStats.Record((int)expected, true);
                if (TrainingStats.Completed % 180 == 0)
                {
                    presenceManifest.Flush();
                    Debug.Log($"[송장 유무 데이터셋] {TrainingStats.Completed}/{targetBoxes}개 상자 · {TrainingStats.Completed * 5}장");
                }
                if (TrainingStats.Completed >= targetBoxes)
                {
                    EvaluationFinished = true;
                    trainingClock.Stop();
                    presenceManifest.Dispose(); presenceManifest = null;
                    File.WriteAllText(Path.Combine(presenceDatasetPath, "complete.json"),
                        $"{{\"samples\":{TrainingStats.Completed},\"seed\":{evaluationSeed},\"size\":96}}");
                    Debug.Log("[송장 유무 데이터셋 완료] " + presenceDatasetPath);
                }
                else OnEpisodeBegin();
                return;
            }
            BoxFace face;
            if (trainingMode && integrationCell != null)
            {
                var box = sample.GetComponent<BoxInfo>();
                if (box == null || !integrationCell.TryJudgeFace(box))
                    throw new InvalidOperationException("CellController를 통한 ONNX 판정에 실패했습니다.");
                face = box.judgedFace;
                var integrationCheck = GetComponent<InferenceIntegrationCheck>();
                if (integrationCheck != null) integrationCheck.VerifyRotation(box, face);
            }
            else face = presencePairCheck != null ? presencePairCheck.Judge(rig, presenceDetector, currentLabel, expected) : presenceDetector.Judge(rig);
            OnActionReceived(new ActionBuffers(ActionSegment<float>.Empty, new ActionSegment<int>(new[] { (int)face })));
            return;
        }
        auditPending = trainingMode && auditDecisions && audit != null && audit.Saved < auditSampleLimit && auditSensors.Length == 5;
        if (auditPending)
        {
            auditSample = TrainingStats.Generated;
            geometryFace = FaceJudgeAudit.GeometryFace(sample, currentLabel, out geometryKnown);
            if (TrainingStats.Generated <= 3)
                Debug.Log($"[FacePose 관측] 목표={expected} 실제={geometryFace} root={sample.transform.rotation} label={currentLabel?.transform.localRotation}");
            auditLabelPresent = currentLabel != null && currentLabel.activeInHierarchy;
            for (int i = 0; i < 5; i++)
            {
                if (auditSensors[i].Camera != rig.FaceCameras[i] || auditSensors[i].SensorName != $"Face_{i}_{FaceCameraRig.CapturedFaces[i]}")
                    throw new InvalidOperationException("검증 센서의 카메라/면 순서가 일치하지 않습니다.");
                auditSensors[i].RequestCapture(auditSample);
            }
        }
        RequestDecision();
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (!IsPending) return;
        int index = actions.DiscreteActions[0];
        if (index < 0 || index > 5) throw new InvalidOperationException("면 판정 행동은 6개여야 합니다.");
        PredictedFace = (BoxFace)index;
        IsPending = false;
        if (trainingMode)
        {
            bool correct = PredictedFace == expected;
            TrainingStats.Record((int)expected, correct);
            confusion[(int)expected * 6 + index]++;
            if (ConnectedTrainer) trainerJudgments++; else nonTrainerJudgments++;
            bool milestone = !milestoneReported && TrainingStats.Completed >= Mathf.Max(1, targetBoxes);
            if (TrainingStats.Completed % Mathf.Max(1, reportEveryBoxes) == 0 || milestone)
                ReportTrainingProgress();
            if (milestone) milestoneReported = true;
            SetReward(FaceJudgeTrainingStats.RewardFor(correct));
            if (auditPending)
            {
                auditPending = false;
                var behavior = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
                var record = new FaceJudgeAudit.Record
                {
                    sample = auditSample, box = currentBoxName,
                    plannedFace = expected.ToString(), geometryFace = geometryFace.ToString(),
                    predictedFace = PredictedFace.ToString(),
                    policySource = behavior.IsInHeuristicMode() ? "Heuristic (성능 평가 제외)" : ConnectedTrainer ? "Trainer" : "Model Inference",
                    labelPresent = auditLabelPresent, geometryKnown = geometryKnown,
                    geometryMatches = geometryKnown && expected == geometryFace,
                    assignedReward = FaceJudgeTrainingStats.RewardFor(correct),
                    actualAgentReward = GetCumulativeReward(),
                    rewardMatches = Mathf.Approximately(GetCumulativeReward(), FaceJudgeTrainingStats.RewardFor(correct))
                };
                try { audit.Save(record, auditSensors); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException)
                {
                    auditDecisions = false;
                    Debug.LogError("[FaceJudge 검증] 기록 실패 (학습은 계속): " + error.Message, this);
                }
            }
            Academy.Instance.StatsRecorder.Add("FaceJudge/Accuracy", correct ? 1f : 0f);
            Academy.Instance.StatsRecorder.Add("FaceJudge/Recent500Accuracy", (float)TrainingStats.RecentAccuracy);
            Academy.Instance.StatsRecorder.Add("FaceJudge/Accuracy_" + expected, correct ? 1f : 0f);
            if (evaluationOnly && TrainingStats.Completed >= Mathf.Max(1, targetBoxes))
            {
                EvaluationFinished = true;
                trainingClock.Stop();
                SaveEvaluationResult();
            }
            else EndEpisode();
        }
        else
        {
            if (pendingBox != null) pendingBox.judgedFace = PredictedFace;
            pendingBox = null;
            FaceJudged?.Invoke(PredictedFace);
        }
    }

    // 수동 동작 확인 전용. 정답 정보는 관측에 넣지 않는다.
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        actionsOut.DiscreteActions.Array[actionsOut.DiscreteActions.Offset] = (int)(trainingMode ? expected : BoxFace.Bottom);
    }

    bool ConnectedTrainer => Academy.IsInitialized && Academy.Instance.IsCommunicatorOn &&
        GetComponent<Unity.MLAgents.Policies.BehaviorParameters>().BehaviorType == Unity.MLAgents.Policies.BehaviorType.Default;

    string ProgressSummary()
    {
        var stats = TrainingStats;
        int target = Mathf.Max(1, targetBoxes);
        double elapsed = Math.Max(0.001, (trainingClock?.Elapsed.TotalSeconds ?? 0));
        double rate = stats.Completed / elapsed;
        string eta = rate > 0 ? TimeSpan.FromSeconds(Math.Max(0, target - stats.Completed) / rate).ToString(@"hh\:mm\:ss") : "--";
        string status = ConnectedTrainer ? "트레이너 연결됨 · 학습 중 표본 점수" :
            trainerJudgments > 0 ? $"현재 연결 종료 · 트레이너 연결 중 판정 {trainerJudgments:N0}개 / 기타 판정 {nonTrainerJudgments:N0}개" :
            "트레이너 미연결/수동 실행 · 학습 성능으로 사용 금지";
        if (ConnectedTrainer && nonTrainerJudgments > 0) status += $" (기타 판정 {nonTrainerJudgments:N0}개 포함)";
        if (evaluationOnly) status = "고정 ONNX 평가 · 학습 없음 · " + (EvaluationFinished ? "완료" : "실행 중");
        if (presenceDetector != null) status = "카메라별 송장 유무 ONNX · " + (EvaluationFinished ? "평가 완료" : "실행 중");
        if (exportPresenceDataset) status = "송장 유무 데이터셋 생성 · 정확도/보상은 모델 성능 아님 · " + (EvaluationFinished ? "완료" : "실행 중");
        var text = new StringBuilder();
        text.AppendLine(status);
        if (exportPresenceDataset)
        {
            text.AppendLine($"상자 {stats.Completed:N0}/{target:N0} · 사진 {stats.Completed * 5:N0}장");
            text.AppendLine("학습/검증/테스트 사진과 정답을 저장합니다.");
            text.Append(presenceDatasetPath);
            return text.ToString();
        }
        if (presenceDetector != null)
        {
            for (int i = 0; i < 5; i++) text.Append($"{i}:{FaceCameraRig.CapturedFaces[i]}={presenceDetector.Scores[i]:F3}  ");
            text.AppendLine(" → " + presenceDetector.PredictedFace);
        }
        text.AppendLine("현재 상자: " + currentBoxName + " (한 개씩 판정)");
        text.AppendLine($"박스 생성 {stats.Generated:N0}개 | 판정 완료 {stats.Completed:N0} / {target:N0} ({Math.Min(100, 100.0 * stats.Completed / target):F1}%)");
        text.AppendLine($"정답 {stats.Correct:N0} / 오답 {stats.Completed - stats.Correct:N0} | 누적 정확도 {stats.Accuracy:P1} | 최근 500개 {stats.RecentAccuracy:P1}");
        text.AppendLine($"점수 {stats.Score:+0.0;-0.0;0.0} (정답 +1 / 오답 -0.8) | 평균 보상 {stats.MeanReward:F3}");
        string elapsedText = TimeSpan.FromSeconds(elapsed).ToString(@"hh\:mm\:ss");
        text.AppendLine($"경과 {elapsedText} | {rate:F1}개/초 | 목표까지 예상 {eta}");
        for (int i = 0; i < 6; i++)
            text.Append($"{(BoxFace)i}: {stats.FaceCorrect[i]}/{stats.FaceTotal[i]}  ");
        text.AppendLine();
        text.Append(evaluationOnly ? "6개 면을 같은 개수로 평가합니다. 정확도로 모델을 비교하세요." : stats.Completed >= target ? "목표 도달 · 학습 종료/ONNX 저장은 Python 트레이너가 관리합니다." : "진행률은 현재 Play 세션 기준입니다.");
        return text.ToString();
    }

    void OnGUI()
    {
        if (trainingMode && auditDecisions) audit?.Draw();
        if (!trainingMode || !showTrainingProgress || TrainingStats == null) return;
        if (progressText == null || Time.unscaledTime >= nextProgressRefresh)
        {
            progressText = ProgressSummary();
            nextProgressRefresh = Time.unscaledTime + 0.25f;
        }
        if (progressStyle == null) progressStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
        // 카메라 미리보기(상단)와 겹치지 않게 하단에 둔다.
        float height = Mathf.Min(275, Screen.height);
        var rect = new Rect(8, Mathf.Max(0, Screen.height - height - 8), Mathf.Min(880, Screen.width - 16), height);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 10, rect.y + 6, rect.width - 20, rect.height - 48), progressText, progressStyle);
        slowPreview = GUI.Toggle(new Rect(rect.x + 10, rect.yMax - 35, rect.width - 20, 28),
            slowPreview, $"천천히 보기 · 상자 한 개를 {previewHoldSeconds:F1}초 유지 (확인 후 끄기)");
    }

    void ReportTrainingProgress()
    {
        if (TrainingStats == null || TrainingStats.Completed == 0 || TrainingStats.Completed == lastSavedCount) return;
        Debug.Log("[FaceJudge 학습 진행] " + ProgressSummary(), this);
        if (reportFailed) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            bool writeHeader = !File.Exists(reportPath);
            using (var writer = new StreamWriter(reportPath, true, new UTF8Encoding(true)))
            {
                if (writeHeader) writer.WriteLine("generated,completed,target,progress_percent,correct,incorrect,accuracy,recent500_accuracy,score,mean_reward,elapsed_seconds,trainer_connected,Top_correct,Top_total,Front_correct,Front_total,Back_correct,Back_total,Left_correct,Left_total,Right_correct,Right_total,Bottom_correct,Bottom_total,trainer_judgments,non_trainer_judgments");
                var st = TrainingStats;
                var values = new List<string>();
                double[] fields = { st.Generated, st.Completed, Mathf.Max(1, targetBoxes), Math.Min(100, 100.0 * st.Completed / Mathf.Max(1, targetBoxes)), st.Correct, st.Completed - st.Correct, st.Accuracy, st.RecentAccuracy, st.Score, st.MeanReward, (trainingClock?.Elapsed.TotalSeconds ?? 0), ConnectedTrainer ? 1 : 0 };
                foreach (double value in fields) values.Add(value.ToString("G", CultureInfo.InvariantCulture));
                for (int i = 0; i < 6; i++) { values.Add(st.FaceCorrect[i].ToString()); values.Add(st.FaceTotal[i].ToString()); }
                values.Add(trainerJudgments.ToString());
                values.Add(nonTrainerJudgments.ToString());
                writer.WriteLine(string.Join(",", values));
            }
            lastSavedCount = TrainingStats.Completed;
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            reportFailed = true;
            Debug.LogWarning("[FaceJudge] 진행 CSV 저장 실패: " + error.Message, this);
        }
    }

    [Serializable]
    class EvaluationResult
    {
        public string model;
        public int seed, completed, correct;
        public double accuracy, meanReward;
        public int[] faceTotal, faceCorrect, confusionMatrix;
    }

    void SaveEvaluationResult()
    {
        var result = new EvaluationResult
        {
            model = presenceDetector != null ? presenceDetector.ModelName : GetComponent<Unity.MLAgents.Policies.BehaviorParameters>().Model.name,
            seed = evaluationSeed, completed = TrainingStats.Completed, correct = TrainingStats.Correct,
            accuracy = TrainingStats.Accuracy, meanReward = TrainingStats.MeanReward,
            faceTotal = TrainingStats.FaceTotal, faceCorrect = TrainingStats.FaceCorrect, confusionMatrix = confusion
        };
        string directory = Path.Combine(Application.dataPath, "..", "results", "face_judge_evaluation");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, result.model + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");
        File.WriteAllText(path, JsonUtility.ToJson(result, true));
        Debug.Log("[FaceJudge 고정 모델 평가 완료] " + ProgressSummary() + "\n" + Path.GetFullPath(path), this);
    }

    void HideSceneBoxes()
    {
        if (!hideSceneBoxesDuringTraining) return;
        // 학습 Agent 초기화 시 한 번만 실행한다. 에셋과 다른 씬의 상자는 건드리지 않는다.
        foreach (var candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (candidate.gameObject.scene != gameObject.scene || !candidate.gameObject.activeSelf) continue;
            if (!candidate.name.StartsWith("ParcelBox", StringComparison.OrdinalIgnoreCase) &&
                candidate.GetComponent<BoxInfo>() == null) continue;
            // 리그/Agent의 조상을 끄면 학습 자체가 멈추므로 제외한다.
            if (transform == candidate || transform.IsChildOf(candidate) ||
                rig.transform == candidate || rig.transform.IsChildOf(candidate)) continue;
            hiddenSceneBoxes.Add(candidate.gameObject);
            candidate.gameObject.SetActive(false);
        }
    }

    void RestoreSceneBoxes()
    {
        foreach (var original in hiddenSceneBoxes) if (original != null) original.SetActive(true);
        hiddenSceneBoxes.Clear();
    }

    void ClearSample()
    {
        if (sample != null)
        {
            // Destroy는 프레임 끝까지 지연된다. 이전 상자가 영상에 함께 남지 않게 한다.
            sample.SetActive(false);
            Destroy(sample);
            sample = null;
        }
        foreach (var asset in temporaryAssets) if (asset != null) Destroy(asset);
        temporaryAssets.Clear();
    }

    protected override void OnDisable()
    {
        if (trainingMode)
        {
            trainingClock?.Stop();
            ReportTrainingProgress();
        }
        decisionQueued = false;
        IsPending = false;
        pendingBox = null;
        ClearSample();
        base.OnDisable();
        RestoreSceneBoxes();
        audit?.Dispose();
        presenceManifest?.Dispose(); presenceManifest = null;
    }
}
