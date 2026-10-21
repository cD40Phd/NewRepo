using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace TypeStrike.Game;

public sealed class WordBank
{
    static WordBank? _english;
    static WordBank? _russian;

    readonly Dictionary<int, List<string>> _byLength = new();
    readonly int _minLength;
    readonly int _maxLength;

    public string Id { get; }
    public string Name { get; }
    /// <summary>Раскладка клавиатуры, на которую стоит переключиться (например, "ru-RU").</summary>
    public string? KeyboardCulture { get; }
    public HashSet<char> Alphabet { get; } = new();
    public int Count { get; }
    public bool IsCyrillic => Alphabet.Any(c => c is >= 'а' and <= 'я');
    public IEnumerable<string> AllWords => _byLength.Values.SelectMany(list => list);

    public static WordBank English => _english ??= FromResource("Words.en.txt", "en", "English", "en-US");
    public static WordBank Russian => _russian ??= FromResource("Words.ru.txt", "ru", "Русский", "ru-RU");

    WordBank(string id, string name, string? keyboardCulture, IEnumerable<string> words, IEnumerable<char>? alphabet = null)
    {
        Id = id;
        Name = name;
        KeyboardCulture = keyboardCulture;

        foreach (var word in words.Distinct())
        {
            if (!_byLength.TryGetValue(word.Length, out var list))
                _byLength[word.Length] = list = new List<string>();
            list.Add(word);
            foreach (var ch in word)
                Alphabet.Add(TextUtil.Norm(ch));
            Count++;
        }

        if (Count == 0)
            throw new InvalidDataException("В списке нет ни одного подходящего слова.");
        if (alphabet != null)
            Alphabet.UnionWith(alphabet);

        _minLength = _byLength.Keys.Min();
        _maxLength = _byLength.Keys.Max();
    }

    public static WordBank FromText(string text, string id, string name, string? keyboardCulture)
    {
        var words = Regex.Split(text.Normalize(NormalizationForm.FormC), @"[^\p{L}]+")
            .Where(w => w.Length is >= 1 and <= 20)
            .Select(w => w.ToLowerInvariant());
        return new WordBank(id, name, keyboardCulture, words);
    }

    /// <summary>
    /// Набор из готовых слов. <paramref name="alphabet"/> расширяет «свои» буквы —
    /// нужно урокам, чтобы буквы вне урока не считались чужой раскладкой.
    /// </summary>
    public static WordBank FromWords(IEnumerable<string> words, string id, string name, string? keyboardCulture,
        IEnumerable<char>? alphabet = null) =>
        new(id, name, keyboardCulture, words, alphabet);

    public static WordBank FromFile(string path)
    {
        var text = File.ReadAllText(path);
        bool cyrillic = text.Any(c => c is (>= 'А' and <= 'я') or 'ё' or 'Ё');
        return FromText(text, "file:" + Path.GetFileName(path).ToLowerInvariant(),
            Path.GetFileNameWithoutExtension(path), cyrillic ? "ru-RU" : "en-US");
    }

    static WordBank FromResource(string resource, string id, string name, string culture)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Ресурс {resource} не найден.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return FromText(reader.ReadToEnd(), id, name, culture);
    }

    /// <summary>
    /// Случайное слово длиной min..max. Старается не повторять слова на экране
    /// и первые буквы целей, чтобы первая нажатая буква однозначно выбирала цель.
    /// </summary>
    public string Pick(int min, int max, ICollection<char> avoidFirstLetters, ICollection<string> avoidWords, Random rng)
    {
        min = Math.Clamp(min, _minLength, _maxLength);
        max = Math.Clamp(max, min, _maxLength);

        var lengths = new List<int>();
        for (int len = min; len <= max; len++)
            if (_byLength.ContainsKey(len))
                lengths.Add(len);
        if (lengths.Count == 0)
            lengths.Add(_byLength.Keys.OrderBy(l => Math.Abs(l - (min + max) / 2.0)).First());

        string? fallback = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            var list = _byLength[lengths[rng.Next(lengths.Count)]];
            var word = list[rng.Next(list.Count)];
            if (avoidWords.Contains(word))
                continue;
            fallback ??= word;
            if (!avoidFirstLetters.Contains(TextUtil.Norm(word[0])))
                return word;
        }
        return fallback ?? _byLength[lengths[0]][rng.Next(_byLength[lengths[0]].Count)];
    }
}
