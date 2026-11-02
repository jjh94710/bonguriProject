using UnityEditor;
using UnityEngine;
[CustomEditor(typeof(TotoBase))]
public sealed class TotoBaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); var tote = (TotoBase)target;
        EditorGUILayout.LabelField("적재", tote.Count + " / " + tote.Capacity);
        EditorGUILayout.HelpBox("5×6cm 면만 흡착합니다. 아래층 4개 → 위층 4개 순서이며, 위층은 바로 아래 박스의 지지를 확인합니다.", MessageType.Info);
        if (tote.loader != null) EditorGUILayout.HelpBox(tote.loader.Status, MessageType.None);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || tote.loader == null || tote.loader.Busy || tote.loader.Attached || tote.AutoStacking))
            if (GUILayout.Button("다음 박스 5×6면 흡착 → 2×2×2 적재")) tote.StackNextBox();
        using (new EditorGUI.DisabledScope(!Application.isPlaying || tote.loader == null || tote.loader.Busy || tote.loader.Attached || tote.AutoStacking || tote.Count >= tote.Capacity))
            if (GUILayout.Button("8개 연속 적재 시험 (같은 위치에 다음 박스 생성)")) tote.StartAutoStack();
        using (new EditorGUI.DisabledScope(!Application.isPlaying || !tote.AutoStacking))
            if (GUILayout.Button("연속 적재 중단 (흡착 유지)")) tote.StopAutoStack();
        if (Application.isPlaying) Repaint();
    }
}
