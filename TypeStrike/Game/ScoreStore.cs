using System.IO;
using System.Text.Json;

namespace TypeStrike.Game;

public sealed class BestScore
{
    public int Score { get; set; }
    public int Wave { get; set; }
    public double Wpm { get; set; }
    public double Accuracy { get; set; }
    public DateTime Date { get; set; }
}

/// <summary>Рекорды по каждому набору слов и сложности в %AppData%\TypeStrike\scores.json.</summary>
public sealed class ScoreStore
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TypeStrike", "scores.json");

    public Dictionary<string, BestScore> Best { get; set; } = new();

    public static ScoreStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ScoreStore>(File.ReadAllText(FilePath)) ?? new ScoreStore();
        }
        catch
        {
            // повреждённый файл — начинаем с чистого листа
        }
        return new ScoreStore();
    }

    public BestScore? Get(string key) => Best.GetValueOrDefault(key);

    /// <summary>Возвращает true, если результат стал новым рекордом.</summary>
    public bool Submit(string key, GameResult result)
    {
        if (Best.TryGetValue(key, out var best) && best.Score >= result.Score)
            return false;

        Best[key] = new BestScore
        {
            Score = result.Score,
            Wave = result.Wave,
            Wpm = result.Wpm,
            Accuracy = result.Accuracy,
            Date = DateTime.Now,
        };
        Save();
        return result.Score > 0;
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // не удалось сохранить — не критично
        }
    }
}
