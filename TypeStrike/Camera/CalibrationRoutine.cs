namespace TypeStrike.Camera;

/// <summary>
/// Сценарий калибровки: точка плавно перелетает по 9 позициям экрана
/// и задерживается на каждой. Игрок поворачивает голову, указывая носом на точку;
/// пока точка стоит, кадры камеры записываются как «голова направлена сюда».
/// </summary>
public sealed class CalibrationRoutine
{
    public const double IntroSeconds = 3.0;
    const double MoveSeconds = 0.9;
    const double DwellSeconds = 1.2;
    /// <summary>Голове нужно время, чтобы довернуться к точке, — первые доли секунды остановки не записываем.</summary>
    const double SettleSeconds = 0.5;

    public static readonly (double X, double Y)[] Points =
    {
        (0.5, 0.5), (0.1, 0.1), (0.5, 0.1), (0.9, 0.1), (0.9, 0.5),
        (0.9, 0.9), (0.5, 0.9), (0.1, 0.9), (0.1, 0.5), (0.5, 0.5),
    };

    static double SegmentSeconds => MoveSeconds + DwellSeconds;
    public static double TotalSeconds => IntroSeconds + Points.Length * SegmentSeconds;

    public double Time { get; private set; }

    public void Update(double dt) => Time += dt;

    public bool IsIntro => Time < IntroSeconds;
    public bool IsDone => Time >= TotalSeconds;
    public double Progress => Math.Clamp(Time / TotalSeconds, 0, 1);
    public int IntroCountdown => (int)Math.Ceiling(IntroSeconds - Time);

    int Segment => Math.Clamp((int)((Time - IntroSeconds) / SegmentSeconds), 0, Points.Length - 1);
    double SegmentTime => Time - IntroSeconds - Segment * SegmentSeconds;

    /// <summary>Записывать ли сейчас кадры: точка стоит и глаза успели на неё перевестись.</summary>
    public bool Collecting => !IsIntro && !IsDone && SegmentTime >= MoveSeconds + SettleSeconds;

    /// <summary>Положение точки в долях окна.</summary>
    public (double X, double Y) Target
    {
        get
        {
            if (IsIntro)
                return Points[0];
            if (IsDone)
                return Points[^1];
            int i = Segment;
            var from = i == 0 ? Points[0] : Points[i - 1];
            var to = Points[i];
            double t = Math.Clamp(SegmentTime / MoveSeconds, 0, 1);
            t = t * t * (3 - 2 * t); // плавный разгон и торможение
            return (from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
        }
    }

    /// <summary>Точка стоит на месте (для анимации «сжатия» мишени).</summary>
    public bool Dwelling => !IsIntro && !IsDone && SegmentTime >= MoveSeconds;
}
