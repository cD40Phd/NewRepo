namespace TypeStrike.Game;

public enum Finger { LeftPinky, LeftRing, LeftMiddle, LeftIndex, Thumb, RightIndex, RightMiddle, RightRing, RightPinky }

/// <summary>Клавиша экранной клавиатуры. Координаты и ширина — в «клавишах» (1 = обычная клавиша).</summary>
public sealed record KeyCap(string Label, char? Char, double X, int Row, double Width, Finger? Finger, bool HomeBump);

/// <summary>Раскладка ЙЦУКЕН / QWERTY с зонами пальцев по методике слепой печати.</summary>
public sealed class KeyboardLayout
{
    const Finger LP = Finger.LeftPinky, LR = Finger.LeftRing, LM = Finger.LeftMiddle, LI = Finger.LeftIndex;
    const Finger RI = Finger.RightIndex, RM = Finger.RightMiddle, RR = Finger.RightRing, RP = Finger.RightPinky;

    static readonly Finger[] Row0Fingers = { LP, LP, LR, LM, LI, LI, RI, RI, RM, RR, RP, RP, RP };
    static readonly Finger[] Row1Fingers = { LP, LR, LM, LI, LI, RI, RI, RM, RR, RP, RP, RP };
    static readonly Finger[] Row2Fingers = { LP, LR, LM, LI, LI, RI, RI, RM, RR, RP, RP };
    static readonly Finger[] Row3Fingers = { LP, LR, LM, LI, LI, RI, RI, RM, RR, RP };

    public const double Width = 15;
    public const int Rows = 5;

    readonly Dictionary<char, KeyCap> _byChar = new();

    public static KeyboardLayout English { get; } = new(
        "`1234567890-=", "qwertyuiop[]", "asdfghjkl;'", "zxcvbnm,./", 'f', 'j');

    public static KeyboardLayout Russian { get; } = new(
        "ё1234567890-=", "йцукенгшщзхъ", "фывапролджэ", "ячсмитьбю.", 'а', 'о');

    public IReadOnlyList<KeyCap> Keys { get; }

    KeyboardLayout(string row0, string row1, string row2, string row3, char bumpLeft, char bumpRight)
    {
        var keys = new List<KeyCap>();

        AddRow(keys, row0, 0, 0, Row0Fingers, bumpLeft, bumpRight);
        keys.Add(new KeyCap("⌫", null, 13, 0, 2, RP, false));

        keys.Add(new KeyCap("Tab", null, 0, 1, 1.5, LP, false));
        AddRow(keys, row1, 1, 1.5, Row1Fingers, bumpLeft, bumpRight);
        keys.Add(new KeyCap("\\", null, 13.5, 1, 1.5, RP, false));

        keys.Add(new KeyCap("Caps", null, 0, 2, 1.75, LP, false));
        AddRow(keys, row2, 2, 1.75, Row2Fingers, bumpLeft, bumpRight);
        keys.Add(new KeyCap("Enter", null, 12.75, 2, 2.25, RP, false));

        keys.Add(new KeyCap("Shift", null, 0, 3, 2.25, LP, false));
        AddRow(keys, row3, 3, 2.25, Row3Fingers, bumpLeft, bumpRight);
        keys.Add(new KeyCap("Shift", null, 12.25, 3, 2.75, RP, false));

        keys.Add(new KeyCap("Ctrl", null, 0, 4, 1.5, null, false));
        keys.Add(new KeyCap("Win", null, 1.5, 4, 1.25, null, false));
        keys.Add(new KeyCap("Alt", null, 2.75, 4, 1.25, null, false));
        keys.Add(new KeyCap("", ' ', 4, 4, 6.5, Finger.Thumb, false));
        keys.Add(new KeyCap("Alt", null, 10.5, 4, 1.25, null, false));
        keys.Add(new KeyCap("Win", null, 11.75, 4, 1.25, null, false));
        keys.Add(new KeyCap("Ctrl", null, 13, 4, 2, null, false));

        Keys = keys;
        foreach (var key in keys)
            if (key.Char is char c)
                _byChar[c] = key;
    }

    static void AddRow(List<KeyCap> keys, string chars, int row, double offset, Finger[] fingers, char bumpLeft, char bumpRight)
    {
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            keys.Add(new KeyCap(char.ToUpperInvariant(c).ToString(), c, offset + i, row, 1, fingers[i],
                c == bumpLeft || c == bumpRight));
        }
    }

    public static KeyboardLayout For(WordBank bank) => bank.IsCyrillic ? Russian : English;

    public KeyCap? Find(char c)
    {
        c = char.ToLowerInvariant(c);
        return _byChar.GetValueOrDefault(c) ?? _byChar.GetValueOrDefault(TextUtil.Norm(c));
    }

    public static string FingerName(Finger finger) => finger switch
    {
        Finger.LeftPinky => "левый мизинец",
        Finger.LeftRing => "левый безымянный",
        Finger.LeftMiddle => "левый средний",
        Finger.LeftIndex => "левый указательный",
        Finger.Thumb => "большой палец",
        Finger.RightIndex => "правый указательный",
        Finger.RightMiddle => "правый средний",
        Finger.RightRing => "правый безымянный",
        _ => "правый мизинец",
    };
}
