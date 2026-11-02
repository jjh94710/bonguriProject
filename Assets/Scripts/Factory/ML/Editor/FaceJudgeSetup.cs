using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

public static class FaceJudgeSetup
{
    [MenuItem("Tools/Factory ML/현재 씬에 측면 보조 조명 추가")]
    static void SetupLighting()
    {
        if (Application.isPlaying) { Debug.LogWarning("조명 저장은 Play를 정지한 뒤 실행하세요."); return; }
        foreach (var rig in Object.FindObjectsByType<FaceCameraRig>(FindObjectsInactive.Include))
        {
            Undo.RegisterFullObjectHierarchyUndo(rig.gameObject, "Build Face Fill Lights");
            rig.BuildFillLights();
            EditorUtility.SetDirty(rig);
            EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        }
        Debug.Log("[FaceCameraRig] 측면 보조 조명 4개 추가/갱신 완료 (그림자 없음).");
    }

    [MenuItem("Tools/Factory ML/선택한 리그에 면 판정 Agent 설정")]
    static void Setup()
    {
        var rig = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<FaceCameraRig>() : null;
        if (rig == null) { Debug.LogError("FaceCameraRig가 있는 오브젝트를 선택하세요."); return; }
        if (Application.isPlaying) return;
        Undo.RegisterFullObjectHierarchyUndo(rig.gameObject, "Setup Face Judge");
        rig.BuildRig();
        var behavior = rig.GetComponent<BehaviorParameters>();
        if (behavior == null) behavior = Undo.AddComponent<BehaviorParameters>(rig.gameObject);
        behavior.BehaviorName = "FaceJudge";
        behavior.BrainParameters.VectorObservationSize = 0;
        behavior.BrainParameters.NumStackedVectorObservations = 1;
        behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(6);
        behavior.UseChildSensors = false;
        // 모두 같은 오브젝트에 붙여 다른 자식 카메라가 관측에 섞이지 않게 한다.
        var sensors = rig.GetComponents<CameraSensorComponent>();
        if (sensors.Length != 0 && sensors.Length != FaceCameraRig.FaceCount)
        { Debug.LogError("기존 CameraSensorComponent 개수를 확인하세요. 0개 또는 5개여야 합니다."); return; }
        for (int i = 0; i < FaceCameraRig.FaceCount; i++)
        {
            var sensor = sensors.Length == 0 ? null : sensors[i] as AuditedFaceCameraSensor;
            if (sensor == null)
            {
                if (sensors.Length != 0) Undo.DestroyObjectImmediate(sensors[i]);
                sensor = Undo.AddComponent<AuditedFaceCameraSensor>(rig.gameObject);
            }
            sensor.Camera = rig.FaceCameras[i];
            sensor.SensorName = $"Face_{i}_{FaceCameraRig.CapturedFaces[i]}";
            sensor.Width = rig.Resolution;
            sensor.Height = rig.Resolution;
            sensor.Grayscale = false;
            sensor.ObservationStacks = 1;
            EditorUtility.SetDirty(sensor);
        }
        var agent = rig.GetComponent<FaceJudgeAgent>();
        if (agent == null) agent = Undo.AddComponent<FaceJudgeAgent>(rig.gameObject);
        var target = new SerializedObject(agent);
        target.FindProperty("rig").objectReferenceValue = rig;
        var generator = rig.GetComponent<LabelDatasetGenerator>();
        if (generator != null)
        {
            var source = new SerializedObject(generator);
            target.CopyFromSerializedProperty(source.FindProperty("boxes"));
            target.CopyFromSerializedProperty(source.FindProperty("labelChildName"));
            target.CopyFromSerializedProperty(source.FindProperty("labelPlacement"));
        }
        target.ApplyModifiedProperties();
        EditorUtility.SetDirty(rig);
        EditorUtility.SetDirty(behavior);
        EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        Debug.Log("면 판정 설정 완료. 학습용 별도 씬에서 Training Mode를 켜세요. 공정에서는 끄고 ONNX를 연결하세요.", agent);
    }
}
