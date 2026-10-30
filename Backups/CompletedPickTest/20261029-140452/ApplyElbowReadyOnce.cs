using System.IO;
using UnityEditor;
[InitializeOnLoad]
public static class ApplyElbowReadyOnce
{
    static ApplyElbowReadyOnce() { EditorApplication.delayCall += Apply; }
    static void Apply()
    {
        if (File.Exists("Temp/RobotReadyPoseElbowUp.json")) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Apply; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SetRobotReadyPose.Apply();
    }
}
