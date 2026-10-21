using System.Windows;
using System.Windows.Media;

namespace TypeStrike.Game;

public enum EnemyKind
{
    /// <summary>Обычный корабль со словом средней длины.</summary>
    Drone,
    /// <summary>Быстрый «дротик» с коротким словом, летит зигзагом.</summary>
    Dart,
    /// <summary>Медленный носитель с длинным словом, выпускает мини-корабли.</summary>
    Carrier,
    /// <summary>Мини-корабль из носителя: 2–3 буквы, очень быстрый.</summary>
    Mite,
    /// <summary>Флагман каждой пятой волны: очень длинное слово.</summary>
    Boss,
}

public static class EnemyPalette
{
    public static Color ColorOf(EnemyKind kind) => kind switch
    {
        EnemyKind.Drone => Color.FromRgb(0x7A, 0xE7, 0xFF),
        EnemyKind.Dart => Color.FromRgb(0xFF, 0xD1, 0x66),
        EnemyKind.Carrier => Color.FromRgb(0xB3, 0x88, 0xFF),
        EnemyKind.Mite => Color.FromRgb(0xFF, 0x6B, 0x6B),
        EnemyKind.Boss => Color.FromRgb(0xFF, 0x3D, 0x81),
        _ => Colors.White,
    };

    public static double RadiusOf(EnemyKind kind) => kind switch
    {
        EnemyKind.Drone => 16,
        EnemyKind.Dart => 12,
        EnemyKind.Carrier => 26,
        EnemyKind.Mite => 9,
        EnemyKind.Boss => 46,
        _ => 16,
    };
}

public sealed class Enemy
{
    public required string Word { get; init; }
    public required EnemyKind Kind { get; init; }
    public Point Pos;
    public Vector Knock;
    public double Speed;
    public double Radius;
    public Color Color;
    /// <summary>Сколько букв уже напечатано (выстрелов сделано).</summary>
    public int Typed;
    /// <summary>Сколько пуль долетело.</summary>
    public int Hits;
    public double Flash;
    public double Age;
    public double Heading;
    public double WobblePhase;
    public double SpawnCooldown;
    public bool IsAlive = true;

    public bool CanBeTargeted => IsAlive && Typed < Word.Length;

    // Кэш отрисовки подписи
    internal FormattedText? Label;
    internal int LabelTyped = -1;
}

public sealed class Bullet
{
    public Point Pos;
    public Vector Dir;
    public required Enemy Target { get; init; }
    public bool Done;
}

public sealed class Particle
{
    public Point Pos;
    public Vector Vel;
    public double Life;
    public double MaxLife;
    public double Size;
    public Color Color;
}

public sealed class Ring
{
    public Point Center;
    public double StartRadius;
    public double EndRadius;
    public double Life;
    public double MaxLife;
    public double Thickness;
    public Color Color;

    public double Progress => 1 - Life / MaxLife;
    public double Radius => StartRadius + (EndRadius - StartRadius) * Math.Sqrt(Progress);
}

public sealed class Star
{
    // Координаты нормированы (0..1), чтобы не зависеть от размера окна
    public double X;
    public double Y;
    public double Speed;
    public double Size;
    public double Brightness;
}

public sealed class Banner
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public double Duration { get; init; }
    public Color Color { get; init; }
    public double Time;
}

public static class TextUtil
{
    /// <summary>Нижний регистр и «ё» = «е», чтобы не наказывать за букву ё.</summary>
    public static char Norm(char c)
    {
        c = char.ToLowerInvariant(c);
        return c == 'ё' ? 'е' : c;
    }
}
