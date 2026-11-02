using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.InferenceEngine;
using Unity.MLAgents.Policies;

public static class FaceJudgeEvaluationSetup
{
    [MenuItem("Tools/Factory ML/모델 평가/15000스텝 모델")]
    static void Mid() => Prepare("results/face_judge_quick_reward08_01/FaceJudge/FaceJudge-14999.onnx", "FaceJudge_14999");
    [MenuItem("Tools/Factory ML/모델 평가/20000스텝 모델")]
    static void Final() => Prepare("results/face_judge_quick_reward08_01/FaceJudge/FaceJudge-20001.onnx", "FaceJudge_20001");
    [MenuItem("Tools/Factory ML/모델 평가/5000스텝 모델")]
    static void Short() => Prepare("results/face_judge_5000_04/FaceJudge.onnx", "FaceJudge_5000");
    [MenuItem("Tools/Factory ML/모델 평가/학습 모드로 복귀")]
    static void Training()
    {
        if (Application.isPlaying) return;
        var agent = Object.FindFirstObjectByType<FaceJudgeAgent>();
        if (agent == null) return;
        var serialized = new SerializedObject(agent);
        serialized.FindProperty("exportPresenceDataset").boolValue = false;
        serialized.FindProperty("presenceDetector").objectReferenceValue = null;
        serialized.FindProperty("evaluationOnly").boolValue = false;
        serialized.FindProperty("targetBoxes").intValue = 20000;
        serialized.ApplyModifiedProperties();
        var policy = agent.GetComponent<BehaviorParameters>();
        policy.Model = null;
        policy.BehaviorType = BehaviorType.Default;
        EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
    }
    static void Prepare(string source, string name)
    {
        if (Application.isPlaying) { Debug.LogError("Play를 정지한 뒤 모델을 선택하세요."); return; }
        if (!File.Exists(source)) { Debug.LogError("모델 파일 없음: " + source); return; }
        var agent = Object.FindFirstObjectByType<FaceJudgeAgent>();
        if (agent == null) { Debug.LogError("FaceJudgeAgent가 없습니다."); return; }
        const string folder = "Assets/FactoryMLModels";
        Directory.CreateDirectory(folder);
        string target = folder + "/" + name + ".onnx";
        File.Copy(source, target, true);
        AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(target);
        if (model == null) { Debug.LogError("ONNX 가져오기 실패"); return; }
        var serialized = new SerializedObject(agent);
        serialized.FindProperty("exportPresenceDataset").boolValue = false;
        serialized.FindProperty("presenceDetector").objectReferenceValue = null;
        serialized.FindProperty("trainingMode").boolValue = true;
        serialized.FindProperty("evaluationOnly").boolValue = true;
        serialized.FindProperty("targetBoxes").intValue = 360;
        serialized.FindProperty("slowPreview").boolValue = false;
        serialized.FindProperty("auditDecisions").boolValue = true;
        serialized.ApplyModifiedProperties();
        var policy = agent.GetComponent<BehaviorParameters>();
        policy.Model = model;
        policy.BehaviorType = BehaviorType.InferenceOnly;
        policy.InferenceDevice = InferenceDevice.Burst;
        policy.DeterministicInference = true;
        EditorUtility.SetDirty(policy);
        EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
        Selection.activeGameObject = agent.gameObject;
        Debug.Log("[FaceJudge] " + name + " 연결 완료. Play로 360개 고정 모델 평가 (학습 없음).");
    }
}
