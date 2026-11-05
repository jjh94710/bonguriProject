using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildBonguriPlayer
{
    [MenuItem("Tools/Bonguri/Build/Windows x64")]
    public static void BuildWindows()
    {
        string destination = Path.GetFullPath("Builds/Windows/Bonguri.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/bongyuri.unity" },
            locationPathName = destination,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        Directory.CreateDirectory("Logs");
        var summary = report.summary;
        File.WriteAllText("Logs/WindowsBuildResult.txt",
            $"Result: {summary.result}\nOutput: {destination}\nBytes: {summary.totalSize}\nDuration: {summary.totalTime}\nErrors: {summary.totalErrors}\nWarnings: {summary.totalWarnings}\n");
        if (summary.result != BuildResult.Succeeded)
        {
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw new Exception("Windows build failed. See Logs/WindowsBuild.log.");
        }
        Debug.Log("Windows build complete: " + destination);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
