using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.InferenceEngine;
using Unity.MLAgents.Policies;

public static class LabelPresenceSetup
{
    [MenuItem("Tools/Factory ML/송장 유무/1. 데이터셋 1800상자 생성 준비")]
    static void Dataset()
    {
        var agent = Configure();
        if (agent == null) return;
        var edit = new SerializedObject(agent);
        edit.FindProperty("exportPresenceDataset").boolValue = true;
        edit.FindProperty("targetBoxes").intValue = 1800;
        edit.FindProperty("presenceDetector").objectReferenceValue = null;
        edit.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
        Debug.Log("[송장 유무] Play하면 1800상자/9000장을 생성합니다. 완료 후 Play를 정지하세요.");
    }

    [MenuItem("Tools/Factory ML/송장 유무/2. ONNX 연결 및 360상자 평가 준비")]
    static void Evaluate()
    {
        const string source = "results/label_presence/label_presence.onnx";
        const string localConfig = "results/label_presence/deployment.json";
        const string bundledConfig = "Assets/FactoryMLModels/LabelPresence.deployment.json";
        const string destination = "Assets/FactoryMLModels/LabelPresence.onnx";
        bool useLocal = File.Exists(source) && File.Exists(localConfig);
        if (!useLocal && (!File.Exists(destination) || !File.Exists(bundledConfig)))
        { Debug.LogError("포함된 송장 유무 모델이 없습니다. scripts/train_label_presence.py를 실행하세요."); return; }
        var agent = Configure();
        if (agent == null) return;
        const string folder = "Assets/FactoryMLModels";
        Directory.CreateDirectory(folder);
        if (useLocal)
        {
            File.Copy(source, destination, true);
            File.Copy(localConfig, bundledConfig, true);
            AssetDatabase.ImportAsset(bundledConfig, ImportAssetOptions.ForceSynchronousImport);
        }
        AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(destination);
        if (model == null) throw new System.InvalidOperationException("송장 유무 ONNX 가져오기 실패");
        var detector = agent.GetComponent<LabelPresenceDetector>();
        if (detector == null) detector = Undo.AddComponent<LabelPresenceDetector>(agent.gameObject);
        var detectorEdit = new SerializedObject(detector);
        detectorEdit.FindProperty("model").objectReferenceValue = model;
        detectorEdit.FindProperty("threshold").floatValue = JsonUtility.FromJson<Deployment>(File.ReadAllText(bundledConfig)).threshold;
        detectorEdit.ApplyModifiedProperties();
        var edit = new SerializedObject(agent);
        edit.FindProperty("exportPresenceDataset").boolValue = false;
        edit.FindProperty("presenceDetector").objectReferenceValue = detector;
        edit.FindProperty("evaluationSeed").intValue = 20261104;
        edit.FindProperty("targetBoxes").intValue = 360;
        edit.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(agent.gameObject.scene);
        Debug.Log("[송장 유무] ONNX 연결 완료. Play하면 새 시드로 360개 상자를 평가합니다. Python 연결 불필요.");
    }
    [System.Serializable] class Deployment { public float threshold; }

    static FaceJudgeAgent Configure()
    {
        if (Application.isPlaying) { Debug.LogError("Play를 정지하고 실행하세요."); return null; }
        var agent = Object.FindAnyObjectByType<FaceJudgeAgent>();
        if (agent == null) { Debug.LogError("현재 씬에 FaceJudgeAgent가 없습니다."); return null; }
        Undo.RecordObject(agent, "Configure Label Presence");
        var edit = new SerializedObject(agent);
        edit.FindProperty("trainingMode").boolValue = true;
        edit.FindProperty("evaluationOnly").boolValue = true;
        edit.FindProperty("evaluationSeed").intValue = 20261103;
        edit.FindProperty("slowPreview").boolValue = false;
        edit.FindProperty("auditDecisions").boolValue = false;
        edit.ApplyModifiedProperties();
        var policy = agent.GetComponent<BehaviorParameters>();
        policy.Model = null;
        policy.BehaviorType = BehaviorType.HeuristicOnly;
        EditorUtility.SetDirty(policy);
        Selection.activeGameObject = agent.gameObject;
        return agent;
    }
}
