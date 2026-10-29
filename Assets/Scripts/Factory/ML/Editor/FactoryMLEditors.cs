using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>FR-03 ① 카메라 리그를 버튼으로 세우고 바로 확인한다.</summary>
[CustomEditor(typeof(FaceCameraRig))]
public class FaceCameraRigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var rig = (FaceCameraRig)target;
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            rig.IsBuilt
                ? "카메라 5대가 있다. 값을 바꾼 뒤에는 다시 눌러 배치를 갱신한다."
                : "아직 카메라가 없다. '카메라 5대 배치'를 누른다.",
            rig.IsBuilt ? MessageType.Info : MessageType.Warning);

        if (rig.SeesEverything)
        {
            EditorGUILayout.HelpBox(
                "Culling Mask 가 모든 레이어를 보고 있어 로봇암·컨베이어까지 찍힌다.\n" +
                "아래 버튼으로 상자 전용 레이어만 보게 하면 배경이 깨끗해진다.",
                MessageType.Warning);
        }

        if (GUILayout.Button("카메라 5대 배치 / 갱신", GUILayout.Height(26)))
        {
            Undo.RegisterFullObjectHierarchyUndo(rig.gameObject, "Build Face Camera Rig");
            rig.BuildRig();
            MarkDirty(rig);
        }

        if (GUILayout.Button($"상자 레이어('{SampleBox.DefaultLayerName}')만 보기"))
        {
            int layer = EnsureLayer(SampleBox.DefaultLayerName);
            if (layer < 0)
            {
                EditorUtility.DisplayDialog("레이어 없음",
                    "빈 레이어 칸이 없다. Project Settings > Tags and Layers 에서 하나 비우고 다시 누른다.", "확인");
            }
            else
            {
                serializedObject.Update();
                serializedObject.FindProperty("cullingMask").intValue = 1 << layer;
                serializedObject.ApplyModifiedProperties();
                rig.BuildRig();
                MarkDirty(rig);
                Debug.Log($"[FaceCameraRig] Culling Mask → '{SampleBox.DefaultLayerName}' (layer {layer}) 만 보도록 바꿨다.", rig);
            }
        }

        using (new EditorGUI.DisabledScope(!rig.IsBuilt))
        {
            if (GUILayout.Button("5면 찍어보기 (임시 상자 세우고 촬영)"))
            {
                PreviewCapture(rig);
            }
        }

        if (GUILayout.Button("카메라 지우기"))
        {
            Undo.RegisterFullObjectHierarchyUndo(rig.gameObject, "Clear Face Camera Rig");
            foreach (var cam in rig.FaceCameras)
            {
                if (cam != null) Undo.DestroyObjectImmediate(cam.gameObject);
            }
            MarkDirty(rig);
        }
    }

    /// <summary>임시 상자를 송장이 위로 오게 세워두고 5면을 찍는다.</summary>
    static void PreviewCapture(FaceCameraRig rig)
    {
        var temps = new System.Collections.Generic.List<Object>();
        GameObject box = null;
        Texture2D[] shots = null;

        try
        {
            int layer = SampleBox.ResolveLayer(SampleBox.DefaultLayerName);
            box = SampleBox.Create(rig.BoxSize, layer, temps);
            box.transform.position = rig.BoxCenter;
            box.transform.rotation = Quaternion.FromToRotation(Vector3.forward, Vector3.up);

            shots = rig.Capture();

            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "LabelDataset", "preview"));
            Directory.CreateDirectory(dir);

            var ratios = new float[FaceCameraRig.FaceCount];
            var line = new StringBuilder();

            for (int i = 0; i < FaceCameraRig.FaceCount; i++)
            {
                if (shots[i] == null) continue;
                ratios[i] = rig.LabelRatio(shots[i]);
                BoxFace face = FaceCameraRig.CapturedFaces[i];
                if (line.Length > 0) line.Append("  |  ");
                line.Append($"{face} {ratios[i]:P1}");
                File.WriteAllBytes(Path.Combine(dir, $"preview_{face}.png"), shots[i].EncodeToPNG());
            }

            // 콘솔 목록은 두 줄만 보여주므로 5면 값을 한 줄에 모은다.
            Debug.Log($"[FaceCameraRig] {line}  →  판정 {rig.JudgeFace(ratios)}\nPNG: {dir}", rig);
        }
        finally
        {
            FaceCameraRig.ReleaseShots(shots);
            foreach (var t in temps) SampleBox.Kill(t);
            SampleBox.Kill(box);
        }
    }

    /// <summary>레이어가 없으면 빈 칸에 만든다. 못 만들면 -1.</summary>
    static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;

        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return -1;

        var so = new SerializedObject(assets[0]);
        var layers = so.FindProperty("layers");
        if (layers == null) return -1;

        // 0 ~ 7 은 유니티 예약 레이어라 건드리지 않는다.
        for (int i = 8; i < layers.arraySize; i++)
        {
            var element = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(element.stringValue))
            {
                element.stringValue = name;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return i;
            }
        }
        return -1;
    }

    static void MarkDirty(Object o)
    {
        EditorUtility.SetDirty(o);
        if (!Application.isPlaying) EditorSceneManager.MarkAllScenesDirty();
    }
}

/// <summary>게임 화면 미리보기 패널(UI-01)을 버튼으로 만든다.</summary>
[CustomEditor(typeof(FacePreviewPanel))]
public class FacePreviewPanelEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var panel = (FacePreviewPanel)target;
        EditorGUILayout.Space();

        if (panel.Rig == null)
        {
            EditorGUILayout.HelpBox("FaceCameraRig 를 연결한다.", MessageType.Error);
        }
        else if (!panel.Rig.IsBuilt)
        {
            EditorGUILayout.HelpBox("리그에 카메라가 없다. 먼저 '카메라 5대 배치'.", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Play 를 누르면 게임 화면 구석에 5면이 뜬다.\n" +
                "Culling Mask 가 상자 레이어만 보므로 여기 보이는 그림이 곧 CNN 입력이다.",
                MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(panel.Rig == null || !panel.Rig.IsBuilt))
        {
            if (GUILayout.Button("미리보기 패널 만들기 / 갱신", GUILayout.Height(26)))
            {
                Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Build Face Preview Panel");
                panel.Build();
                EditorUtility.SetDirty(panel);
                if (!Application.isPlaying) EditorSceneManager.MarkAllScenesDirty();
            }

            if (GUILayout.Button("지금 한 번 찍기"))
            {
                panel.Refresh();
                SceneView.RepaintAll();
            }
        }

        if (GUILayout.Button("패널 지우기"))
        {
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "Clear Face Preview Panel");
            panel.Clear();
            EditorUtility.SetDirty(panel);
            if (!Application.isPlaying) EditorSceneManager.MarkAllScenesDirty();
        }
    }
}

/// <summary>FR-03 ① 학습 이미지셋을 버튼으로 만든다.</summary>
[CustomEditor(typeof(LabelDatasetGenerator))]
public class LabelDatasetGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var gen = (LabelDatasetGenerator)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("저장 위치", gen.DatasetPath, EditorStyles.wordWrappedMiniLabel);

        if (GUILayout.Button("학습 이미지 생성", GUILayout.Height(28)))
        {
            int n = 0;
            try
            {
                EditorUtility.DisplayProgressBar("학습 이미지", "5면 촬영 중…", 0.5f);
                n = gen.Generate();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (n > 0) EditorUtility.RevealInFinder(gen.DatasetPath);
        }

        using (new EditorGUI.DisabledScope(!Directory.Exists(gen.DatasetPath)))
        {
            if (GUILayout.Button("데이터셋 폴더 열기"))
            {
                EditorUtility.RevealInFinder(gen.DatasetPath);
            }
        }
    }
}
