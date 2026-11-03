using System;
using System.Collections.Generic;

/// <summary>한 Play 세션의 실제 판정 완료 개수로 집계한다. trainer step과 구분한다.</summary>
public sealed class FaceJudgeTrainingStats
{
    public int Generated { get; private set; }
    public int Completed { get; private set; }
    public int Correct { get; private set; }
    public const double CorrectReward = 1.0;
    public const double IncorrectReward = -0.8;
    public static float RewardFor(bool correct) => (float)(correct ? CorrectReward : IncorrectReward);
    public double Score => Correct * CorrectReward + (Completed - Correct) * IncorrectReward;
    public double Accuracy => Completed == 0 ? 0 : (double)Correct / Completed;
    public double MeanReward => Completed == 0 ? 0 : (double)Score / Completed;
    public double RecentAccuracy => recent.Count == 0 ? 0 : (double)recentCorrect / recent.Count;
    public readonly int[] FaceTotal = new int[6];
    public readonly int[] FaceCorrect = new int[6];
    readonly Queue<bool> recent = new Queue<bool>();
    int recentCorrect;
    public void RecordGenerated() => Generated++;
    public void Record(int expected, bool correct)
    {
        if (expected < 0 || expected >= 6) throw new ArgumentOutOfRangeException(nameof(expected));
        Completed++;
        FaceTotal[expected]++;
        if (correct) { Correct++; FaceCorrect[expected]++; recentCorrect++; }
        recent.Enqueue(correct);
        if (recent.Count > 500 && recent.Dequeue()) recentCorrect--;
    }
}
