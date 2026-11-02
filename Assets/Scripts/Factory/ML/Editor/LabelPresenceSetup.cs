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

    [MenuItem("Tools/Factory ML/송장 유무/3. Inference Engine 공정 연동 검증 준비")]
    static void Integration()
    {
        if (Application.isPlaying) { Debug.LogError("Play를 정지하세요."); return; }
        Evaluate();
        var agent = Object.FindAnyObjectByType<FaceJudgeAgent>();
        var rig = agent != null ? agent.GetComponent<FaceCameraRig>() : null;
        var detector = agent != null ? agent.GetComponent<LabelPresenceDetector>() : null;
        if (rig == null || detector == null || !detector.HasModel) return;
        const string scene = "Assets/Scenes/InferenceEngineValidation.unity";
        if (!EditorSceneManager.SaveScene(agent.gameObject.scene, scene)) return;
        var cellObject = GameObject.Find("InferenceValidationCell");
        if (cellObject == null) cellObject = new GameObject("InferenceValidationCell");
        var cell = cellObject.GetComponent<CellController>();
        if (cell == null) cell = cellObject.AddComponent<CellController>();
        var cellEdit = new SerializedObject(cell);
        cellEdit.FindProperty("settings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CellSettings>("Assets/_Factory/CellSettings.asset");
        cellEdit.FindProperty("faceRig").objectReferenceValue = rig;
        cellEdit.FindProperty("faceDetector").objectReferenceValue = detector;
        cellEdit.ApplyModifiedProperties();
        var edit = new SerializedObject(agent);
        edit.FindProperty("integrationCell").objectReferenceValue = cell;
        edit.FindProperty("evaluationSeed").intValue = 20261105;
        edit.ApplyModifiedProperties();
        var check = agent.GetComponent<InferenceIntegrationCheck>();
        if (check == null) check = agent.gameObject.AddComponent<InferenceIntegrationCheck>();
        var checkEdit = new SerializedObject(check);
        checkEdit.FindProperty("agent").objectReferenceValue = agent;
        checkEdit.FindProperty("rig").objectReferenceValue = rig;
        checkEdit.FindProperty("detector").objectReferenceValue = detector;
        checkEdit.FindProperty("cell").objectReferenceValue = cell;
        checkEdit.ApplyModifiedProperties();
        EditorSceneManager.SaveScene(agent.gameObject.scene);
        Debug.Log("[Inference Engine] 별도 검증 씬 준비 완료. Play하면 CellController 경유 360상자와 오류 처리를 검사합니다.");
    }

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
        edit.FindProperty("integrationCell").objectReferenceValue = null;
        edit.ApplyModifiedProperties();
        var policy = agent.GetComponent<BehaviorParameters>();
        policy.Model = null;
        policy.BehaviorType = BehaviorType.HeuristicOnly;
        EditorUtility.SetDirty(policy);
        Selection.activeGameObject = agent.gameObject;
        return agent;
    }
}
