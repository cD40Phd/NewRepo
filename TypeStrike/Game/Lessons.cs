using System.Text;

namespace TypeStrike.Game;

/// <summary>
/// Урок по методике слепой печати: начинаем с основного ряда (ФЫВА–ОЛДЖ / ASDF–JKL)
/// и в каждом уроке добавляем по 1–2 новые буквы, закрепляя уже изученные.
/// </summary>
public sealed record Lesson(int Number, string NewLetters, string Letters, string Tip)
{
    public string Title => $"Урок {Number}: {string.Join(" ", NewLetters.ToUpperInvariant().ToCharArray())}";
}

/// <summary>Что нужно движку, чтобы провести урок.</summary>
public sealed class LessonPlan
{
    public required Lesson Lesson { get; init; }
    /// <summary>Упражнения на новые буквы: «ааа», «оао», «вол»… — первая волна.</summary>
    public required WordBank Drills { get; init; }
    /// <summary>Слова только из изученных букв (дополненные сочетаниями, если слов мало).</summary>
    public required WordBank Words { get; init; }
    public int Waves { get; init; } = 4;
    public required string Key { get; init; }
}

public static class LessonCatalog
{
    public const double PassAccuracy = 90;

    public static readonly string[] Tips =
    {
        "Пальцы на основном ряду: левая рука — ФЫВА, правая — ОЛДЖ. На А и О есть выступы — найдите их на ощупь.",
        "Не смотрите на клавиатуру — смотрите на экран и подсказку внизу.",
        "Точность важнее скорости: скорость придёт сама, а привычку к ошибкам потом трудно исправить.",
        "Каждой клавише — свой палец. После нажатия возвращайте палец на основной ряд.",
        "Сядьте ровно, локти под прямым углом, запястья не лежат на столе.",
        "Печатайте ритмично — с одинаковыми паузами между нажатиями.",
        "Лучше 15–20 минут каждый день, чем час раз в неделю.",
        "Устали — сделайте перерыв и разомните кисти.",
        "Пробел — большим пальцем, Enter и Backspace — правым мизинцем.",
        "Нажимайте клавиши легко и коротко, не давите на них.",
    };

    static readonly string[] RussianSteps =
        { "ао", "вл", "ыд", "фж", "пр", "ке", "нг", "ми", "ть", "уш", "сб", "цщ", "чю", "яэ", "йзхъ", "ё" };

    static readonly string[] EnglishSteps =
        { "fj", "dk", "sl", "a", "gh", "ei", "ru", "ty", "wo", "qp", "nv", "mc", "bx", "z" };

    public static IReadOnlyList<Lesson> Russian { get; } = Build(RussianSteps);
    public static IReadOnlyList<Lesson> English { get; } = Build(EnglishSteps);

    public static IReadOnlyList<Lesson> For(WordBank bank) => bank.Id switch
    {
        "ru" => Russian,
        "en" => English,
        _ => Array.Empty<Lesson>(),
    };

    static IReadOnlyList<Lesson> Build(string[] steps)
    {
        var lessons = new List<Lesson>();
        var letters = "";
        for (int i = 0; i < steps.Length; i++)
        {
            letters += steps[i];
            lessons.Add(new Lesson(i + 1, steps[i], letters, Tips[i % Tips.Length]));
        }
        return lessons;
    }

    public static LessonPlan CreatePlan(WordBank source, Lesson lesson)
    {
        var rng = new Random(lesson.Number * 7919 + source.Id.GetHashCode(StringComparison.Ordinal));
        var known = lesson.Letters.Select(TextUtil.Norm).ToHashSet();
        var fresh = lesson.NewLetters.ToCharArray();
        var all = lesson.Letters.ToCharArray();
        var id = $"{source.Id}-lesson{lesson.Number}";

        var drills = new HashSet<string>();
        for (int i = 0; drills.Count < 60 && i < 2000; i++)
        {
            double kind = rng.NextDouble();
            string drill = kind switch
            {
                // «ааа», «ооо» — привыкаем к новой клавише
                < 0.25 => new string(fresh[rng.Next(fresh.Length)], 3),
                // только новые буквы: «оа», «аоа», «оаоа»
                < 0.6 => Combo(rng, fresh, fresh, 2 + rng.Next(3)),
                // новые вперемешку с изученными
                _ => Combo(rng, fresh, all, 3 + rng.Next(3)),
            };
            drills.Add(drill);
        }

        var words = source.AllWords.Where(w => w.All(c => known.Contains(TextUtil.Norm(c)))).ToList();
        // Если слов с новыми буквами хватает — тренируем именно их
        var freshSet = fresh.Select(TextUtil.Norm).ToHashSet();
        var focused = words.Where(w => w.Any(c => freshSet.Contains(TextUtil.Norm(c)))).ToList();
        if (focused.Count >= 30)
            words = focused;
        var extra = new HashSet<string>();
        for (int i = 0; words.Count + extra.Count < 80 && i < 4000; i++)
            extra.Add(Combo(rng, fresh, all, 3 + rng.Next(rng.NextDouble() < 0.2 ? 6 : 3)));
        words.AddRange(extra);

        return new LessonPlan
        {
            Lesson = lesson,
            Key = id,
            Drills = WordBank.FromWords(drills, id + "-drill", lesson.Title, source.KeyboardCulture, source.Alphabet),
            Words = WordBank.FromWords(words, id, lesson.Title, source.KeyboardCulture, source.Alphabet),
        };
    }

    /// <summary>Буквосочетание, где примерно половина букв — новые, и нет трёх одинаковых подряд.</summary>
    static string Combo(Random rng, char[] fresh, char[] pool, int length)
    {
        var sb = new StringBuilder(length);
        for (int attempts = 0; sb.Length < length && attempts < 100; attempts++)
        {
            var src = rng.NextDouble() < 0.5 ? fresh : pool;
            char c = src[rng.Next(src.Length)];
            if (sb.Length >= 2 && sb[^1] == c && sb[^2] == c)
                continue;
            sb.Append(c);
        }
        // Гарантируем хотя бы одну новую букву
        if (!sb.ToString().Any(fresh.Contains))
            sb[rng.Next(sb.Length)] = fresh[rng.Next(fresh.Length)];
        return sb.ToString();
    }
}
