using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class TotoPlayTest
{
    static bool running, requested; static int last; static double since, next; static BoxAttacher loader; static ToteBase tote;
    static Vector3 source; static float surface; static bool errors;
    static TotoPlayTest() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!running)
        {
            if (!File.Exists("Temp/TotoTest.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete("Temp/TotoTest.request"); running = true; requested = false; last = 0; loader = null; errors = false;
            since = EditorApplication.timeSinceStartup; File.WriteAllText("C:/Users/Codelab/Documents/UnityBackups/bonguriProject/SuctionCrash/TotoTestResult.txt", "RUNNING\n");
            Application.logMessageReceived += Log; EditorApplication.isPaused = false; EditorApplication.isPlaying = true; return;
        }
        if (!EditorApplication.isPlaying) return;
        EditorApplication.isPaused = false; Application.runInBackground = true; Time.timeScale = 1; EditorApplication.QueuePlayerLoopUpdate();
        if (Time.time < 1) return;
        try
        {
            if (loader == null)
            {
                tote = UnityEngine.Object.FindObjectsByType<ToteBase>().Single(); loader = tote.loader;
                source = loader.targetBox.bounds.center; surface = loader.targetBox.bounds.min.y; next = Time.time + .5;
            }
            File.WriteAllText("C:/Users/Codelab/Documents/UnityBackups/bonguriProject/SuctionCrash/TotoTestProgress.txt", "sim=" + Time.time + " count=" + tote.Count + "/8 attached=" + loader.Attached + " status=" + loader.Status + " IK=" + loader.armIK.Status + " tcp=" + loader.tcp.position + " box=" + loader.targetBox.bounds.center);
            if (Time.time > diagAt) { diagAt = Time.time + 2; Diagnostics(); }
            if (tote.Count == 8)
            {
                bool stable = tote.Boxes.All(b => b != null && tote.Upright(b) && b.attachedRigidbody.linearVelocity.magnitude < .04f);
                Finish(stable && !errors, "eight boxes checked; stable=" + stable); return;
            }
            if (tote.Count > last)
            {
                last = tote.Count; File.AppendAllText("C:/Users/Codelab/Documents/UnityBackups/bonguriProject/SuctionCrash/TotoTestResult.txt", "placed=" + last + " pos=" + loader.PlacedBoxes[last - 1].bounds.center + "\n");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefabs/ParcelBox_ProcessVariant.prefab");
                Quaternion[] rotations = { Quaternion.identity, Quaternion.Euler(0, 90, 0), Quaternion.Euler(-90, 0, 0), Quaternion.Euler(0, 180, 0) };
                var nextBox = UnityEngine.Object.Instantiate(prefab, source, rotations[last % rotations.Length]); nextBox.name = "TotoTestParcel_" + (last + 1);
                var box = nextBox.GetComponent<BoxCollider>(); Physics.SyncTransforms();
                nextBox.transform.position += new Vector3(source.x - box.bounds.center.x, surface + box.bounds.extents.y - box.bounds.center.y, source.z - box.bounds.center.z);
                nextBox.GetComponent<Rigidbody>().position = nextBox.transform.position; Physics.SyncTransforms(); loader.targetBox = box;
                requested = false; next = Time.time + 1;
            }
            if (!requested && Time.time >= next) { tote.StackNextBox(); requested = true; }
            else if (requested && !loader.Busy) { Finish(false, loader.Status); return; }
            if (EditorApplication.timeSinceStartup - since > 720) Finish(false, "test timeout");
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }
    static float diagAt;
    static void Diagnostics()
    {
        var k = new ArmKinematics(loader.transform, loader.tcp); var q = k.CurrentAngles();
        k.Forward(q, out var fp, out _);
        string text = "FK=" + Vector3.Distance(fp, loader.tcp.position) + " goal=" + loader.armIK.GoalPosition + " tcp=" + loader.tcp.position + "\n";
        for (int i = 0; i < q.Length; i++) text += k.Joints[i].name + " actual=" + q[i] + " drive=" + k.Joints[i].xDrive.target + "\n";
        foreach (var c in loader.GetComponentsInChildren<Collider>())
        {
            if (!c.enabled || c.isTrigger) continue;
            foreach (var other in Physics.OverlapBox(c.bounds.center, c.bounds.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (other == c || Physics.GetIgnoreCollision(c, other) || other.transform.IsChildOf(loader.transform)) continue;
                if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, other, other.transform.position, other.transform.rotation, out _, out float depth) && depth > .0005f)
                    text += c.transform.parent.name + "/" + c.name + " vs " + other.transform.parent?.name + "/" + other.name + " depth=" + depth + "\n";
            }
        }
        File.WriteAllText("Temp/TotoContacts.txt", text);
    }
    static void Log(string text, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors = true; File.AppendAllText("C:/Users/Codelab/Documents/UnityBackups/bonguriProject/SuctionCrash/TotoTestResult.txt", text + "\n" + stack + "\n"); } }
    static void Finish(bool pass, string message)
    {
        File.AppendAllText("C:/Users/Codelab/Documents/UnityBackups/bonguriProject/SuctionCrash/TotoTestResult.txt", "PASS=" + pass + " count=" + (tote == null ? 0 : tote.Count) + " " + message + "\n");
        running = false; Application.logMessageReceived -= Log; EditorApplication.isPlaying = false;
    }
}



