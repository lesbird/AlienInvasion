using System.Text.Json;

namespace AlienInvasion.Game;

/// <summary>End-of-level score and letter grade.</summary>
public class LevelResult
{
    public int Level, Score, KillPts, BossPts, SecretPts, TimePts, AccPts, DeathPts, Deaths;
    public float KillPct, Accuracy, Time, Par;
    public string Grade = "D";
    public bool NewBest;
    public BestScore Previous;
}

public class BestScore
{
    public int Score { get; set; }
    public string Grade { get; set; } = "D";
    public float Time { get; set; }
}

public static class Scoring
{
    static string ScoresPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlienInvasion", "scores.json");
    static readonly string[] Grades = { "S", "A", "B", "C", "D" };
    public static bool ReadOnly;   // debug/test runs never touch the saved best scores

    /// <summary>Target clear time in seconds; deeper levels are larger, boss levels need a fight.</summary>
    public static float Par(Level l) => 75 + l.Number * 10f + (l.BossType >= 0 ? 60 : 0);

    public static LevelResult Compute(World w, int deaths)
    {
        var l = w.L;
        var r = new LevelResult { Level = l.Number, Deaths = deaths, Time = w.LevelTime, Par = Par(l) };
        r.KillPct = w.TotalEnemies > 0 ? (float)w.Kills / w.TotalEnemies : 1;
        r.Accuracy = w.ShotsFired >= 10 ? Math.Clamp((float)w.ShotsHit / w.ShotsFired, 0, 1) : 0.5f;
        float secretR = l.SecretCount > 0 ? (float)w.SecretsFound / l.SecretCount : 1;
        float timeR = Math.Clamp(r.Par / MathF.Max(1, r.Time), 0, 1);

        r.KillPts = w.Kills * 100;
        r.BossPts = w.Boss != null && w.Boss.Dead ? 2500 : 0;
        r.SecretPts = w.SecretsFound * 750;
        r.TimePts = Math.Max(0, (int)((r.Par * 1.5f - r.Time) * 20));
        r.AccPts = (int)(r.Accuracy * 2000);
        r.DeathPts = -deaths * 1000;
        r.Score = Math.Max(0, r.KillPts + r.BossPts + r.SecretPts + r.TimePts + r.AccPts + r.DeathPts);

        float g = 0.35f * r.KillPct + 0.25f * secretR + 0.25f * timeR + 0.15f * r.Accuracy - 0.08f * deaths;
        r.Grade = g >= 0.9f ? "S" : g >= 0.78f ? "A" : g >= 0.64f ? "B" : g >= 0.48f ? "C" : "D";

        // best scores per sublevel
        var best = LoadBest();
        best.TryGetValue(l.Number, out r.Previous);
        if (r.Previous == null || r.Score > r.Previous.Score)
        {
            r.NewBest = true;
            var keepGrade = r.Previous != null && Array.IndexOf(Grades, r.Previous.Grade) < Array.IndexOf(Grades, r.Grade) ? r.Previous.Grade : r.Grade;
            best[l.Number] = new BestScore { Score = r.Score, Grade = keepGrade, Time = r.Previous != null ? MathF.Min(r.Previous.Time, r.Time) : r.Time };
            if (!ReadOnly) SaveBest(best);
        }
        return r;
    }

    public static Dictionary<int, BestScore> LoadBest()
    {
        try { return File.Exists(ScoresPath) ? JsonSerializer.Deserialize<Dictionary<int, BestScore>>(File.ReadAllText(ScoresPath)) ?? new() : new(); }
        catch { return new(); }
    }

    static void SaveBest(Dictionary<int, BestScore> best)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScoresPath)!);
            File.WriteAllText(ScoresPath, JsonSerializer.Serialize(best));
        }
        catch { }
    }
}
