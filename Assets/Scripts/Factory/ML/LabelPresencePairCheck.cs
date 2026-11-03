using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>평가 때 같은 상자의 송장만 제거해 검출 결과가 바뀌는지 확인한다.</summary>
public sealed class LabelPresencePairCheck
{
    [Serializable] public class Pair
    {
        public int expected, originalFace, removedFace;
        public float[] originalScores, removedScores;
        public bool passed;
    }
    [Serializable] public class Report
    {
        public int tested, passed;
        public List<Pair> pairs = new List<Pair>();
    }
    readonly Report report = new Report();
    readonly string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "results", "label_presence_pair_check",
        DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8)));

    public BoxFace Judge(FaceCameraRig rig, LabelPresenceDetector detector, GameObject label, BoxFace expected)
    {
        if (report.tested >= 30 || expected == BoxFace.Bottom || label == null || !label.activeSelf)
            return detector.Judge(rig);
        var original = rig.Capture();
        Texture2D[] removed = null;
        try
        {
            var originalFace = detector.Judge(original);
            var pair = new Pair { expected = (int)expected, originalFace = (int)originalFace,
                originalScores = (float[])detector.Scores.Clone() };
            try
            {
                label.SetActive(false);
                removed = rig.Capture();
                pair.removedFace = (int)detector.Judge(removed);
                pair.removedScores = (float[])detector.Scores.Clone();
            }
            finally { label.SetActive(true); }
            // 표시할 점수와 선택 면은 원래 상자 상태로 돌린다.
            detector.Judge(original);
            pair.passed = originalFace == expected && pair.removedFace == (int)BoxFace.Bottom;
            report.tested++;
            if (pair.passed) report.passed++;
            report.pairs.Add(pair);
            Directory.CreateDirectory(folder);
            if (report.tested <= 5)
                for (int i = 0; i < 5; i++)
                {
                    File.WriteAllBytes(Path.Combine(folder, $"{report.tested:D2}_original_{i}.png"), original[i].EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(folder, $"{report.tested:D2}_removed_{i}.png"), removed[i].EncodeToPNG());
                }
            File.WriteAllText(Path.Combine(folder, "report.json"), JsonUtility.ToJson(report, true));
            if (report.tested == 30)
                Debug.Log($"[송장 제거 비교 검증] {report.passed}/{report.tested} 통과 → {folder}");
            return originalFace;
        }
        finally { FaceCameraRig.ReleaseShots(original); FaceCameraRig.ReleaseShots(removed); }
    }
}
