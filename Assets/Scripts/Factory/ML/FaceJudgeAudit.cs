using System;
using System.IO;
using UnityEngine;

/// <summary>같은 샘플의 관측 영상·정답·예측·보상을 저장하고 표시한다.</summary>
public sealed class FaceJudgeAudit : IDisposable
{
    [Serializable] public class Record
    {
        public int sample;
        public string box, plannedFace, geometryFace, predictedFace, policySource;
        public bool labelPresent, geometryKnown, geometryMatches, rewardMatches;
        public float assignedReward, actualAgentReward;
        public string[] sensors;
    }
    public Texture2D[] Images { get; private set; }
    public Record Latest { get; private set; }
    public int Saved { get; private set; }
    public string Folder { get; }
    public FaceJudgeAudit()
    {
        Folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results", "face_judge_audit",
            DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")));
    }

    public static bool TryGetPaperNormal(GameObject sample, GameObject label, out Vector3 normal)
    {
        normal = Vector3.zero;
        if (sample == null || label == null) return false;
        MeshFilter paper = null;
        foreach (var mesh in label.GetComponentsInChildren<MeshFilter>(true))
            if (mesh.name == "LabelPaper") { paper = mesh; break; }
        if (paper == null) paper = label.GetComponent<MeshFilter>();
        if (paper == null || paper.sharedMesh == null) return false;
        var bounds = paper.sharedMesh.bounds;
        Vector3 size = bounds.size;
        int thin = 0;
        float shortest = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 edge = Vector3.zero; edge[axis] = size[axis];
            float length = paper.transform.TransformVector(edge).magnitude;
            if (length < shortest) { shortest = length; thin = axis; }
        }
        Vector3 localNormal = Vector3.zero; localNormal[thin] = 1;
        normal = paper.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(localNormal).normalized;
        if (Vector3.Dot(normal, paper.transform.TransformPoint(bounds.center) - sample.transform.position) < 0) normal = -normal;
        return true;
    }

    public static BoxFace GeometryFace(GameObject sample, GameObject label, out bool known)
    {
        known = false;
        if (label == null) return BoxFace.None;
        if (!label.activeInHierarchy) { known = true; return BoxFace.Bottom; }
        if (!TryGetPaperNormal(sample, label, out Vector3 normal)) return BoxFace.None;
        BoxFace face = BoxFace.Top;
        float best = float.NegativeInfinity;
        for (int i = 0; i < 6; i++)
        {
            float dot = Vector3.Dot(normal, FaceCameraRig.OutwardOf((BoxFace)i));
            if (dot > best) { best = dot; face = (BoxFace)i; }
        }
        known = true;
        return face;
    }

    public void Save(Record record, AuditedFaceCameraSensor[] sensors)
    {
        if (sensors.Length != 5) throw new InvalidOperationException("검증용 카메라 센서가 5개 필요합니다. Agent 설정 메뉴를 다시 실행하세요.");
        string directory = Path.Combine(Folder, record.sample.ToString("D6"));
        record.sensors = new string[5];
        // 누락/다른 샘플의 프레임을 정상 검증으로 저장하지 않는다.
        for (int i = 0; i < 5; i++)
            if (sensors[i].CapturedSample != record.sample || sensors[i].CapturedPng == null)
                throw new InvalidOperationException("실제 센서 프레임이 누락됐습니다: " + sensors[i].SensorName);
        Directory.CreateDirectory(directory);
        var images = new Texture2D[5];
        try
        {
            for (int i = 0; i < 5; i++)
            {
                record.sensors[i] = sensors[i].SensorName;
                File.WriteAllBytes(Path.Combine(directory, sensors[i].SensorName + ".png"), sensors[i].CapturedPng);
                images[i] = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!images[i].LoadImage(sensors[i].CapturedPng)) throw new IOException("관측 PNG 읽기 실패");
                images[i].filterMode = FilterMode.Point;
            }
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonUtility.ToJson(record, true));
        }
        catch
        {
            foreach (var image in images) if (image != null) UnityEngine.Object.Destroy(image);
            throw;
        }
        Dispose();
        Images = images;
        Latest = record;
        Saved++;
        string summary = $"[FaceJudge 검증 #{record.sample}] 배치={record.plannedFace}, 실제 종이 방향={record.geometryFace}, 선택={record.predictedFace}, 보상={record.actualAgentReward:+0.0;-0.0;0.0}, 정답일치={record.geometryMatches}, 보상일치={record.rewardMatches}, 실행={record.policySource}";
        if (!record.geometryKnown || !record.geometryMatches || !record.rewardMatches) Debug.LogWarning(summary + " → " + directory);
        else Debug.Log(summary + " → " + directory);
    }

    public void Draw()
    {
        if (Latest == null || Images == null) return;
        float tile = Mathf.Min(112f, (Screen.width - 24f) / 5f);
        var area = new Rect(8, 165, tile * 5 + 8, tile + 92);
        GUI.Box(area, GUIContent.none);
        GUI.Label(new Rect(area.x + 4, area.y + 2, area.width - 8, 22), $"검증 #{Latest.sample} · 같은 샘플의 실제 센서 프레임 (고정)");
        for (int i = 0; i < 5; i++)
        {
            var rect = new Rect(area.x + 4 + tile * i, area.y + 25, tile - 4, tile - 4);
            GUI.DrawTexture(rect, Images[i], ScaleMode.ScaleToFit);
            GUI.Label(new Rect(rect.x, rect.yMax, tile, 20), FaceCameraRig.CapturedFaces[i].ToString());
        }
        GUI.Label(new Rect(area.x + 4, area.y + tile + 45, area.width - 8, 42),
            $"정답 {Latest.plannedFace} / 실제 {Latest.geometryFace} / 선택 {Latest.predictedFace} / 보상 {Latest.actualAgentReward:+0.0;-0.0;0.0}\n정답 검증 {Latest.geometryMatches} · 보상 검증 {Latest.rewardMatches} · {Latest.policySource}");
    }
    public void Dispose()
    {
        if (Images != null) foreach (var image in Images) if (image != null) UnityEngine.Object.Destroy(image);
        Images = null;
    }
}
