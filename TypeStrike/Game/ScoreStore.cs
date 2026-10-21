using System.IO;
using System.Text.Json;

namespace TypeStrike.Game;

public sealed class LessonAttempt
{
    public double Accuracy { get; set; }
    public double Wpm { get; set; }
    public bool Completed { get; set; }
    public DateTime Date { get; set; }

    public bool Passed => Completed && Accuracy >= LessonCatalog.PassAccuracy;
}

/// <summary>Прогресс освоения урока: серия зачётов подряд и стабильность скорости.</summary>
public sealed record LessonMastery(int Streak, bool StableSpeed, bool Mastered);

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
    /// <summary>Лучшее количество звёзд за урок (ключ — LessonPlan.Key).</summary>
    public Dictionary<string, int> LessonStars { get; set; } = new();
    /// <summary>Последние попытки каждого урока.</summary>
    public Dictionary<string, List<LessonAttempt>> LessonHistory { get; set; } = new();
    /// <summary>Освоенные уроки — после освоения открывается следующая буква.</summary>
    public HashSet<string> MasteredLessons { get; set; } = new();
    /// <summary>Выбранный урок для каждого набора слов (индекс с нуля).</summary>
    public Dictionary<string, int> CurrentLesson { get; set; } = new();

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

    public int StarsFor(string lessonKey) => LessonStars.GetValueOrDefault(lessonKey);

    public void SubmitStars(string lessonKey, int stars)
    {
        if (stars > StarsFor(lessonKey))
        {
            LessonStars[lessonKey] = stars;
            Save();
        }
    }

    public bool IsMastered(string lessonKey) => MasteredLessons.Contains(lessonKey);

    /// <summary>
    /// Записывает попытку. Урок освоен, когда последние <see cref="LessonCatalog.MasteryStreak"/>
    /// попыток подряд — зачёт (точность ≥ 98 %) и скорость в них стабильна.
    /// </summary>
    public LessonMastery AddAttempt(string lessonKey, LessonAttempt attempt)
    {
        if (!LessonHistory.TryGetValue(lessonKey, out var history))
            LessonHistory[lessonKey] = history = new List<LessonAttempt>();
        history.Add(attempt);
        if (history.Count > 20)
            history.RemoveAt(0);

        var mastery = GetMastery(lessonKey);
        if (mastery.Mastered)
            MasteredLessons.Add(lessonKey);
        Save();
        return mastery;
    }

    public LessonMastery GetMastery(string lessonKey)
    {
        bool mastered = MasteredLessons.Contains(lessonKey);
        if (!LessonHistory.TryGetValue(lessonKey, out var history))
            return new LessonMastery(0, false, mastered);

        var streak = new List<LessonAttempt>();
        for (int i = history.Count - 1; i >= 0 && history[i].Passed && streak.Count < LessonCatalog.MasteryStreak; i--)
            streak.Add(history[i]);

        bool stable = streak.Count >= 2 &&
            streak.Min(a => a.Wpm) >= streak.Max(a => a.Wpm) * LessonCatalog.SpeedStability;
        mastered |= streak.Count >= LessonCatalog.MasteryStreak && stable;
        return new LessonMastery(streak.Count, stable, mastered);
    }

    public int GetCurrentLesson(string bankId) => CurrentLesson.GetValueOrDefault(bankId);

    public void SetCurrentLesson(string bankId, int index)
    {
        CurrentLesson[bankId] = index;
        Save();
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
