using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TypeStrike.Game;

/// <summary>
/// Рисует состояние <see cref="GameEngine"/> через DrawingContext.
/// Перерисовывается каждый кадр из MainWindow (CompositionTarget.Rendering).
/// </summary>
public sealed class GameView : FrameworkElement
{
    sealed record EnemyLook(Geometry Shape, Brush Fill, Pen Stroke, Brush Glow);

    static readonly Typeface Mono = new(new FontFamily("Consolas, Courier New"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    static readonly Brush BackgroundBrush = Freeze(new LinearGradientBrush(Color.FromRgb(2, 3, 10), Color.FromRgb(12, 16, 44), 90));
    static readonly Brush NebulaA = Freeze(new RadialGradientBrush(Color.FromArgb(55, 120, 60, 200), Color.FromArgb(0, 120, 60, 200))
    {
        Center = new Point(0.22, 0.28), GradientOrigin = new Point(0.22, 0.28), RadiusX = 0.55, RadiusY = 0.45,
    });
    static readonly Brush NebulaB = Freeze(new RadialGradientBrush(Color.FromArgb(45, 30, 120, 200), Color.FromArgb(0, 30, 120, 200))
    {
        Center = new Point(0.8, 0.65), GradientOrigin = new Point(0.8, 0.65), RadiusX = 0.5, RadiusY = 0.5,
    });

    static readonly Brush TextBrush = Solid(0xF2, 0xF6, 0xFF);
    static readonly Brush TypedBrush = Solid(0xFF, 0x9F, 0x1C);
    static readonly Brush DimBrush = Solid(0x8F, 0xA3, 0xC7);
    static readonly Brush AccentBrush = Solid(0x5C, 0xE1, 0xFF);
    static readonly Brush GreenBrush = Solid(0x6B, 0xFF, 0xB0);
    static readonly Brush HintBrush = Solid(0xFF, 0xD1, 0x66);
    static readonly Brush FlashBrush = Solid(0xFF, 0xFF, 0xFF);
    static readonly Brush LabelBg = Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0x08, 0x0C, 0x1E)));
    static readonly Brush LabelBgTarget = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x2A, 0x1A, 0x08)));
    static readonly Pen LabelPen = Freeze(new Pen(TypedBrush, 1.5));
    static readonly Pen ReticlePen = Freeze(new Pen(TypedBrush, 2) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) });
    static readonly Pen GazePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0x6B, 0xFF, 0xB0))), 2.5));
    static readonly Pen BulletPen = Freeze(new Pen(Solid(0xFF, 0xD0, 0x80), 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    static readonly Pen BulletGlowPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(90, 255, 160, 40))), 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    static readonly Pen ShieldPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(70, 0x6B, 0xFF, 0xB0))), 2));
    static readonly Brush ShipFill = Solid(0x1A, 0x2A, 0x44);
    static readonly Pen ShipPen = Freeze(new Pen(AccentBrush, 2) { LineJoin = PenLineJoin.Round });
    static readonly Brush FlameBrush = Freeze(new RadialGradientBrush(Color.FromArgb(230, 255, 190, 80), Color.FromArgb(0, 255, 80, 20)));
    static readonly Geometry ShipShape = Poly(1, (0, -26), (17, 16), (6, 10), (0, 14), (-6, 10), (-17, 16));
    static readonly Brush[] StarBrushes = Enumerable.Range(0, 8)
        .Select(i => (Brush)Freeze(new SolidColorBrush(Color.FromArgb((byte)(50 + i * 29), 220, 230, 255))))
        .ToArray();
    static readonly EnemyLook[] Looks = Enum.GetValues<EnemyKind>().Select(MakeLook).ToArray();

    readonly Random _rng = new();
    double _dip = 1;

    public GameEngine? Engine { get; set; }

    /// <summary>Масштаб слов на врагах (настройка «Размер букв»).</summary>
    public double TextScale { get; set; } = 1.4;

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Engine?.Resize(sizeInfo.NewSize);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var full = new Rect(0, 0, w, h);
        dc.DrawRectangle(BackgroundBrush, null, full);
        dc.DrawRectangle(NebulaA, null, full);
        dc.DrawRectangle(NebulaB, null, full);

        var g = Engine;
        if (g == null)
            return;
        _dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        DrawStars(dc, g, w, h);

        bool shaking = g.Shake > 0.1;
        if (shaking)
            dc.PushTransform(new TranslateTransform((_rng.NextDouble() - 0.5) * g.Shake, (_rng.NextDouble() - 0.5) * g.Shake));

        foreach (var r in g.Rings)
        {
            var pen = new Pen(new SolidColorBrush(WithAlpha(r.Color, 1 - r.Progress)), r.Thickness * (1 - r.Progress * 0.6));
            dc.DrawEllipse(null, pen, r.Center, r.Radius, r.Radius);
        }

        foreach (var e in g.Enemies)
            if (e.IsAlive)
                DrawEnemy(dc, g, e);

        foreach (var e in g.Enemies)
            if (e.IsAlive && e != g.Target)
                DrawLabel(dc, e, false, w);
        if (g.Target is { IsAlive: true } target)
            DrawLabel(dc, target, true, w);

        foreach (var b in g.Bullets)
        {
            var tail = b.Pos - b.Dir * 16;
            dc.DrawLine(BulletGlowPen, tail, b.Pos);
            dc.DrawLine(BulletPen, tail, b.Pos);
        }

        foreach (var p in g.Particles)
        {
            double k = p.Life / p.MaxLife;
            double s = p.Size * (0.5 + k * 0.5);
            dc.DrawRectangle(new SolidColorBrush(WithAlpha(p.Color, k)), null, new Rect(p.Pos.X - s / 2, p.Pos.Y - s / 2, s, s));
        }

        if (g.Phase is Phase.Intermission or Phase.Fighting or Phase.Finishing)
            DrawShip(dc, g);

        if (shaking)
            dc.Pop();

        if (g.Phase != Phase.Idle)
            DrawHud(dc, g, w, h);

        DrawBanner(dc, g, w, h);

        if (g.Hint != null)
        {
            dc.PushOpacity(Math.Min(1, g.HintTime * 2));
            DrawText(dc, g.Hint, 18, HintBrush, w / 2, h - 140, TextAlignment.Center);
            dc.Pop();
        }

        if (g.MissFlash > 0)
        {
            var c = Color.FromArgb((byte)(Math.Min(1, g.MissFlash) * 140), 255, 60, 80);
            dc.DrawRectangle(null, new Pen(new SolidColorBrush(c), 14), full);
        }
    }

    void DrawStars(DrawingContext dc, GameEngine g, double w, double h)
    {
        foreach (var s in g.Stars)
        {
            var brush = StarBrushes[Math.Clamp((int)(s.Brightness * StarBrushes.Length), 0, StarBrushes.Length - 1)];
            double len = s.Size * (g.Phase == Phase.Intermission ? 3.5 : 1);
            dc.DrawRectangle(brush, null, new Rect(s.X * w, s.Y * h, s.Size, len));
        }
    }

    void DrawEnemy(DrawingContext dc, GameEngine g, Enemy e)
    {
        var look = Looks[(int)e.Kind];
        dc.DrawEllipse(look.Glow, null, e.Pos, e.Radius * 2.2, e.Radius * 2.2);

        dc.PushTransform(new TranslateTransform(e.Pos.X, e.Pos.Y));
        dc.PushTransform(new RotateTransform(e.Heading));
        dc.DrawGeometry(e.Flash > 0 ? FlashBrush : look.Fill, look.Stroke, look.Shape);
        dc.Pop();

        if (e == g.Target)
        {
            dc.PushTransform(new RotateTransform(g.Time * 120));
            dc.DrawEllipse(null, ReticlePen, new Point(0, 0), e.Radius + 9, e.Radius + 9);
            dc.Pop();
        }

        // Зелёное кольцо: камера видит, что взгляд следит за этим врагом
        if (g.GazeOnTarget && e == g.HintEnemy)
        {
            double r = e.Radius + 15 + Math.Sin(g.Time * 6) * 2;
            dc.DrawEllipse(null, GazePen, new Point(0, 0), r, r);
        }
        dc.Pop();
    }

    void DrawLabel(DrawingContext dc, Enemy e, bool isTarget, double width)
    {
        if (e.Label == null || e.LabelTyped != e.Typed || e.LabelScale != TextScale)
        {
            double size = TextScale * e.Kind switch { EnemyKind.Boss => 21, EnemyKind.Mite => 14, _ => 16 };
            var text = new FormattedText(e.Word, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, TextBrush, _dip);
            if (e.Typed > 0)
                text.SetForegroundBrush(TypedBrush, 0, e.Typed);
            e.Label = text;
            e.LabelTyped = e.Typed;
            e.LabelScale = TextScale;
        }

        var ft = e.Label;
        double lw = ft.WidthIncludingTrailingWhitespace, lh = ft.Height;
        double x = Math.Clamp(e.Pos.X - lw / 2, 8, Math.Max(8, width - lw - 8));
        double y = e.Pos.Y + e.Radius + 8;
        dc.DrawRoundedRectangle(isTarget ? LabelBgTarget : LabelBg, isTarget ? LabelPen : null,
            new Rect(x - 6, y - 2, lw + 12, lh + 4), 4, 4);
        dc.DrawText(ft, new Point(x, y));
    }

    void DrawShip(DrawingContext dc, GameEngine g)
    {
        var ship = g.Ship;
        bool blinkOff = g.Invulnerable > 0 && ((int)(g.Time * 12) & 1) == 0;

        if (g.Shields > 1)
            dc.DrawEllipse(null, ShieldPen, ship, 36, 36);
        if (blinkOff)
            return;

        dc.PushTransform(new TranslateTransform(ship.X, ship.Y));
        dc.PushTransform(new RotateTransform(g.ShipAngle));
        double flame = 9 + _rng.NextDouble() * 8;
        dc.DrawEllipse(FlameBrush, null, new Point(0, 16 + flame / 2), 6, flame);
        dc.DrawGeometry(ShipFill, ShipPen, ShipShape);
        dc.DrawEllipse(TypedBrush, null, new Point(0, -3), 3, 3);
        dc.Pop();
        dc.Pop();
    }

    void DrawHud(DrawingContext dc, GameEngine g, double w, double h)
    {
        DrawText(dc, "СЧЁТ", 13, DimBrush, 20, 14);
        DrawText(dc, g.Score.ToString("N0", CultureInfo.CurrentCulture), 30, TextBrush, 20, 30);
        if (g.Combo > 0)
            DrawText(dc, $"x{g.Multiplier}   комбо {g.Combo}", 15, g.Multiplier > 1 ? TypedBrush : DimBrush, 20, 70);

        DrawText(dc, "ВОЛНА", 13, DimBrush, w - 20, 14, TextAlignment.Right);
        DrawText(dc, g.Wave.ToString(CultureInfo.InvariantCulture), 30, AccentBrush, w - 20, 30, TextAlignment.Right);

        if (g.Lesson != null)
        {
            DrawText(dc, "ЦЕЛЬ: ТОЧНОСТЬ 98–100%", 13, DimBrush, w / 2, 14, TextAlignment.Center);
            DrawText(dc, "скорость не важна", 15, DimBrush, w / 2, 32, TextAlignment.Center);
        }
        else if (g.MaxShields > 1)
        {
            var shields = new string('◆', Math.Max(0, g.Shields)) + new string('◇', Math.Max(0, g.MaxShields - g.Shields));
            DrawText(dc, "ЩИТЫ", 13, DimBrush, w / 2, 14, TextAlignment.Center);
            DrawText(dc, shields, 20, GreenBrush, w / 2, 30, TextAlignment.Center);
        }

        DrawText(dc, "ЭМИ  [Enter]", 13, DimBrush, 20, h - 52);
        DrawText(dc, g.Emps > 0 ? new string('●', g.Emps) : "—", 20, g.Emps > 0 ? AccentBrush : DimBrush, 20, h - 34);

        if (g.Lesson != null)
        {
            // В уроке главное — точность, скорость вторична
            var accuracyBrush = g.Accuracy >= LessonCatalog.PassAccuracy ? GreenBrush : TypedBrush;
            DrawText(dc, $"{g.Accuracy:0.#}%", 26, accuracyBrush, w - 20, h - 64, TextAlignment.Right);
            DrawText(dc, $"точность · {g.Wpm:0} WPM", 14, DimBrush, w - 20, h - 30, TextAlignment.Right);
        }
        else
        {
            DrawText(dc, $"{g.Wpm:0} WPM", 22, TextBrush, w - 20, h - 60, TextAlignment.Right);
            DrawText(dc, $"точность {g.Accuracy:0.#}%", 14, DimBrush, w - 20, h - 30, TextAlignment.Right);
        }

        if (g.HintLowered && g.Phase == Phase.Fighting)
            DrawText(dc, "⌨ подсказка спрятана — взгляд следит за целью", 14, GreenBrush, w / 2, h - 26, TextAlignment.Center);

        if (g.LookingDown)
            DrawText(dc, "👀 СМОТРИТЕ НА ЭКРАН", 20, TypedBrush, w / 2, h - 175, TextAlignment.Center);
    }

    void DrawBanner(DrawingContext dc, GameEngine g, double w, double h)
    {
        var b = g.Banner;
        if (b == null)
            return;

        double appear = Math.Min(1, b.Time / 0.25);
        double alpha = Math.Clamp(Math.Min(appear, (b.Duration - b.Time) / 0.4), 0, 1);
        double y = h * 0.34 - (1 - appear) * 24;

        dc.PushOpacity(alpha);
        DrawText(dc, b.Title, 46, new SolidColorBrush(b.Color), w / 2, y, TextAlignment.Center);
        if (b.Subtitle != null)
            DrawText(dc, b.Subtitle, 18, TextBrush, w / 2, y + 62, TextAlignment.Center);
        dc.Pop();
    }

    void DrawText(DrawingContext dc, string text, double size, Brush brush, double x, double y,
        TextAlignment align = TextAlignment.Left)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Mono, size, brush, _dip)
        {
            TextAlignment = align,
        };
        dc.DrawText(ft, new Point(x, y));
    }

    // ───────────────────────────── Геометрия ─────────────────────────────

    static EnemyLook MakeLook(EnemyKind kind)
    {
        var c = EnemyPalette.ColorOf(kind);
        double r = EnemyPalette.RadiusOf(kind);
        Geometry shape = kind switch
        {
            EnemyKind.Drone => Poly(r, (1, 0), (-0.65, 0.8), (-0.3, 0), (-0.65, -0.8)),
            EnemyKind.Dart => Poly(r, (1.15, 0), (-0.85, 0.55), (-0.45, 0), (-0.85, -0.55)),
            EnemyKind.Carrier => Ringed(RegularPoly(r, 6, 0), new EllipseGeometry(new Point(0, 0), r * 0.38, r * 0.38)),
            EnemyKind.Boss => Ringed(RegularPoly(r, 8, 22.5), RegularPoly(r * 0.5, 4, 45)),
            _ => Poly(r, (1, 0), (0, 0.75), (-1, 0), (0, -0.75)),
        };
        return new EnemyLook(
            shape,
            Freeze(new SolidColorBrush(WithAlpha(c, 0.3))),
            Freeze(new Pen(Freeze(new SolidColorBrush(c)), 2) { LineJoin = PenLineJoin.Round }),
            Freeze(new RadialGradientBrush(WithAlpha(c, 0.3), WithAlpha(c, 0))));
    }

    static Geometry Ringed(Geometry outer, Geometry inner)
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(outer);
        group.Children.Add(inner);
        return Freeze(group);
    }

    static Geometry RegularPoly(double r, int sides, double offsetDeg)
    {
        var pts = new (double X, double Y)[sides];
        for (int i = 0; i < sides; i++)
        {
            double a = (offsetDeg + 360.0 * i / sides) * Math.PI / 180;
            pts[i] = (Math.Cos(a) * r, Math.Sin(a) * r);
        }
        return Poly(1, pts);
    }

    static Geometry Poly(double scale, params (double X, double Y)[] pts)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(pts[0].X * scale, pts[0].Y * scale), true, true);
            for (int i = 1; i < pts.Length; i++)
                ctx.LineTo(new Point(pts[i].X * scale, pts[i].Y * scale), true, true);
        }
        return Freeze(geometry);
    }

    static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    static Brush Solid(byte r, byte g, byte b) => Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));

    static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
