using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TypeStrike.Game;

/// <summary>
/// Подсказка в стиле тренажёров слепой печати: текущее слово с выделенной буквой,
/// экранная клавиатура с цветовыми зонами пальцев и руки с подсвеченным нужным пальцем.
/// </summary>
public sealed class KeyboardHint : FrameworkElement
{
    const double Unit = 40;
    const double StripHeight = 46;
    const double HandWidth = 150;
    const double Gap = 20;
    const double KeyboardX = HandWidth + Gap;
    const double KeyboardWidth = KeyboardLayout.Width * Unit;
    const double TotalWidth = KeyboardWidth + 2 * (HandWidth + Gap);
    const double TotalHeight = StripHeight + KeyboardLayout.Rows * Unit + 8;

    static readonly Typeface Mono = new(new FontFamily("Consolas, Courier New"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0x05, 0x07, 0x12)));
    static readonly Pen TopLine = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x3D, 0x5E))), 1));
    static readonly Brush TextBrush = Solid(Color.FromRgb(0xF2, 0xF6, 0xFF));
    static readonly Brush DimBrush = Solid(Color.FromRgb(0x8F, 0xA3, 0xC7));
    static readonly Brush DoneBrush = Solid(Color.FromRgb(0x4E, 0x9C, 0x76));
    static readonly Brush DarkText = Solid(Color.FromRgb(0x05, 0x07, 0x12));
    static readonly Brush KeyIdle = Solid(Color.FromRgb(0x14, 0x1B, 0x2B));
    static readonly Pen KeyIdlePen = Freeze(new Pen(Solid(Color.FromRgb(0x26, 0x31, 0x48)), 1));
    static readonly Brush HandFill = Solid(Color.FromRgb(0x15, 0x1D, 0x2E));
    static readonly Pen PalmPen = Freeze(new Pen(Solid(Color.FromRgb(0x34, 0x44, 0x5F)), 2));
    static readonly Pen ActiveKeyPen = Freeze(new Pen(Brushes.White, 2.5));

    static readonly Color[] FingerColors =
    {
        Color.FromRgb(0xC7, 0x92, 0xEA), // левый мизинец
        Color.FromRgb(0x82, 0xAA, 0xFF), // левый безымянный
        Color.FromRgb(0x7F, 0xD9, 0x8B), // левый средний
        Color.FromRgb(0xFF, 0xCB, 0x6B), // левый указательный
        Color.FromRgb(0xB0, 0xBE, 0xC5), // большие пальцы
        Color.FromRgb(0xF7, 0x8C, 0x6C), // правый указательный
        Color.FromRgb(0x7F, 0xD9, 0x8B), // правый средний
        Color.FromRgb(0x82, 0xAA, 0xFF), // правый безымянный
        Color.FromRgb(0xC7, 0x92, 0xEA), // правый мизинец
    };

    static readonly Brush[] FingerBrushes = FingerColors.Select(c => Solid(c)).ToArray();
    static readonly Brush[] ZoneBrushes = FingerColors.Select(c => Solid(Color.FromArgb(0x38, c.R, c.G, c.B))).ToArray();
    static readonly Pen[] ZonePens = FingerColors.Select(c => Freeze(new Pen(Solid(Color.FromArgb(0x90, c.R, c.G, c.B)), 1.2))).ToArray();
    static readonly Brush[] GlowBrushes = FingerColors
        .Select(c => (Brush)Freeze(new RadialGradientBrush(Color.FromArgb(0xA0, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B))))
        .ToArray();

    // Пальцы левой руки (правая рисуется зеркально): центр X, верх, ширина, длина
    static readonly (Finger Left, Finger Right, double X, double Top, double W, double Len)[] Fingers =
    {
        (Finger.LeftPinky, Finger.RightPinky, 32, 40, 20, 62),
        (Finger.LeftRing, Finger.RightRing, 56, 18, 21, 84),
        (Finger.LeftMiddle, Finger.RightMiddle, 81, 8, 22, 94),
        (Finger.LeftIndex, Finger.RightIndex, 106, 22, 21, 80),
    };

    readonly Dictionary<(string Text, double Size, Brush Brush, double Dip), FormattedText> _textCache = new();
    double _dip = 1;

    public GameEngine? Engine { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        dc.DrawLine(TopLine, new Point(0, 0.5), new Point(w, 0.5));

        var g = Engine;
        if (g == null || w < 10 || h < 10)
            return;
        _dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        double scale = Math.Min(w / TotalWidth, h / TotalHeight);
        dc.PushTransform(new TranslateTransform((w - TotalWidth * scale) / 2, (h - TotalHeight * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));

        var layout = KeyboardLayout.For(g.Words);
        var enemy = g.HintEnemy;
        char? next = enemy != null ? enemy.Word[enemy.Typed] : null;
        var key = next is char c ? layout.Find(c) : null;

        DrawStrip(dc, g, enemy, key, layout);
        DrawKeyboard(dc, layout, key);
        DrawHand(dc, new Point(0, StripHeight), false, key?.Finger);
        DrawHand(dc, new Point(TotalWidth - HandWidth, StripHeight), true, key?.Finger);

        dc.Pop();
        dc.Pop();
    }

    void DrawStrip(DrawingContext dc, GameEngine g, Enemy? enemy, KeyCap? key, KeyboardLayout layout)
    {
        double y = 8;
        if (enemy == null)
        {
            string home = layout == KeyboardLayout.Russian ? "ФЫВА — ОЛДЖ" : "ASDF — JKL;";
            string text = g.Phase == Phase.Fighting
                ? $"Ждём врагов… Пальцы на основном ряду: {home}"
                : $"Пальцы на основном ряду: {home}. Не смотрите на клавиатуру!";
            Draw(dc, text, 17, DimBrush, TotalWidth / 2, y + 6, TextAlignment.Center);
            return;
        }

        double x = KeyboardX;
        if (enemy != g.Target)
        {
            var label = Text("цель: ", 17, DimBrush);
            dc.DrawText(label, new Point(x, y + 6));
            x += label.WidthIncludingTrailingWhitespace;
        }

        double charWidth = Text("0", 26, TextBrush).WidthIncludingTrailingWhitespace;
        for (int i = 0; i < enemy.Word.Length; i++)
        {
            Brush brush = i < enemy.Typed ? DoneBrush : TextBrush;
            if (i == enemy.Typed)
            {
                var fill = key?.Finger is Finger f ? FingerBrushes[(int)f] : TextBrush;
                dc.DrawRoundedRectangle(fill, null, new Rect(x - 2, y - 1, charWidth + 4, 34), 5, 5);
                brush = DarkText;
            }
            dc.DrawText(Text(enemy.Word[i].ToString(), 26, brush), new Point(x, y));
            x += charWidth;
        }

        if (key?.Finger is Finger finger)
        {
            string hint = $"«{char.ToUpperInvariant(enemy.Word[enemy.Typed])}» — {KeyboardLayout.FingerName(finger)}";
            Draw(dc, hint, 17, FingerBrushes[(int)finger], KeyboardX + KeyboardWidth, y + 6, TextAlignment.Right);
        }
    }

    void DrawKeyboard(DrawingContext dc, KeyboardLayout layout, KeyCap? active)
    {
        foreach (var key in layout.Keys)
        {
            var rect = new Rect(KeyboardX + key.X * Unit + 2, StripHeight + key.Row * Unit + 2, key.Width * Unit - 4, Unit - 4);
            bool isActive = key == active;

            if (isActive && key.Finger is Finger af)
            {
                var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                dc.DrawEllipse(GlowBrushes[(int)af], null, center, rect.Width / 2 + 22, rect.Height / 2 + 22);
                dc.DrawRoundedRectangle(FingerBrushes[(int)af], ActiveKeyPen, rect, 6, 6);
            }
            else if (key.Finger is Finger f && key.Char != null)
            {
                dc.DrawRoundedRectangle(ZoneBrushes[(int)f], ZonePens[(int)f], rect, 6, 6);
            }
            else
            {
                dc.DrawRoundedRectangle(KeyIdle, KeyIdlePen, rect, 6, 6);
            }

            if (key.Label.Length > 0)
            {
                bool small = key.Char == null;
                var brush = isActive ? DarkText : small ? DimBrush : TextBrush;
                var ft = Text(key.Label, small ? 11 : 16, brush);
                dc.DrawText(ft, new Point(rect.X + (rect.Width - ft.Width) / 2, rect.Y + (rect.Height - ft.Height) / 2));
            }

            // Выступы на клавишах основного ряда (А/О, F/J)
            if (key.HomeBump)
                dc.DrawRectangle(isActive ? DarkText : DimBrush, null, new Rect(rect.X + rect.Width / 2 - 6, rect.Bottom - 7, 12, 2));
        }
    }

    static void DrawHand(DrawingContext dc, Point origin, bool mirrored, Finger? active)
    {
        dc.PushTransform(mirrored
            ? new MatrixTransform(-1, 0, 0, 1, origin.X + HandWidth, origin.Y)
            : new MatrixTransform(1, 0, 0, 1, origin.X, origin.Y));

        foreach (var (left, right, x, top, width, length) in Fingers)
        {
            var finger = mirrored ? right : left;
            DrawFinger(dc, new Rect(x - width / 2, top, width, length + 20), finger, finger == active);
        }

        dc.PushTransform(new TranslateTransform(118, 150));
        dc.PushTransform(new RotateTransform(-55));
        DrawFinger(dc, new Rect(-11, -62, 22, 70), Finger.Thumb, active == Finger.Thumb);
        dc.Pop();
        dc.Pop();

        dc.DrawRoundedRectangle(HandFill, PalmPen, new Rect(18, 95, 104, 90), 32, 32);
        dc.Pop();
    }

    static void DrawFinger(DrawingContext dc, Rect rect, Finger finger, bool active)
    {
        int i = (int)finger;
        dc.DrawRoundedRectangle(active ? FingerBrushes[i] : HandFill, active ? ActiveKeyPen : ZonePens[i],
            rect, rect.Width / 2, rect.Width / 2);
    }

    // Подсказка перерисовывается каждый кадр — подписи клавиш и букв кэшируем
    FormattedText Text(string text, double size, Brush brush)
    {
        var key = (text, size, brush, _dip);
        if (!_textCache.TryGetValue(key, out var ft))
        {
            if (_textCache.Count > 2000)
                _textCache.Clear();
            ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Mono, size, brush, _dip);
            _textCache[key] = ft;
        }
        return ft;
    }

    void Draw(DrawingContext dc, string text, double size, Brush brush, double x, double y, TextAlignment align)
    {
        var ft = Text(text, size, brush);
        ft.TextAlignment = align;
        dc.DrawText(ft, new Point(x, y));
    }

    static Brush Solid(Color c) => Freeze(new SolidColorBrush(c));

    static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
