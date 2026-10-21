using TypeStrike.Game;

namespace TypeStrike.Camera;

/// <summary>Один кадр калибровки: признаки взгляда и точка, на которую игрок смотрел.</summary>
public sealed record GazeSample(double[] Features, double TargetX, double TargetY, double FaceY, double FaceHeight,
    bool FromGame = false, double Weight = 1);

/// <summary>
/// Модель взгляда: гребневая линейная регрессия из признаков
/// (положение радужки в разрезе век, баланс белка, открытость глаза, положение и размер лица)
/// в нормированные координаты окна (0..1). Точность у веб-камеры скромная,
/// но её хватает, чтобы отличить «смотрит на экран» от «смотрит на клавиатуру».
/// </summary>
public static class GazeModel
{
    /// <summary>
    /// Радужка X и Y в разрезе глаза, открытость (видимая высота радужки к её ширине),
    /// баланс белка слева/справа, центр лица X и Y, ширина лица.
    /// </summary>
    public const int FeatureCount = 7;
    const double Lambda = 0.05;
    /// <summary>Вес игровой точки: она менее надёжна, чем точка калибровки.</summary>
    public const double GameSampleWeight = 0.6;
    /// <summary>Сколько последних игровых точек хранить.</summary>
    public const int MaxGameSamples = 300;

    public static GazeCalibration? Fit(IReadOnlyList<GazeSample> samples, double eyeRate, out string? error)
    {
        error = null;
        int targets = samples.Select(s => (Math.Round(s.TargetX, 2), Math.Round(s.TargetY, 2))).Distinct().Count();
        if (samples.Count < 40 || targets < 6)
        {
            error = "Слишком мало кадров с лицом и глазами. Проверьте освещение и чтобы лицо было в кадре.";
            return null;
        }

        var means = new double[FeatureCount];
        var stds = new double[FeatureCount];
        for (int j = 0; j < FeatureCount; j++)
        {
            means[j] = samples.Average(s => s.Features[j]);
            double variance = samples.Average(s => (s.Features[j] - means[j]) * (s.Features[j] - means[j]));
            stds[j] = Math.Max(Math.Sqrt(variance), 1e-6);
        }

        var rows = samples.Select(s => Design(s.Features, means, stds)).ToArray();
        var weights = samples.Select(s => s.Weight).ToArray();
        var weightsX = Ridge(rows, samples.Select(s => s.TargetX).ToArray(), weights);
        var weightsY = Ridge(rows, samples.Select(s => s.TargetY).ToArray(), weights);

        // Положение головы «смотрю на экран» берём только из калибровки — в игре голова гуляет
        var baseline = samples.Where(s => !s.FromGame).DefaultIfEmpty(samples[0]).ToList();
        var calibration = new GazeCalibration
        {
            FaceY = baseline.Average(s => s.FaceY),
            FaceHeight = baseline.Average(s => s.FaceHeight),
            EyeRate = eyeRate,
            Means = means,
            Stds = stds,
            WeightsX = weightsX,
            WeightsY = weightsY,
        };
        calibration.Samples = samples.Select(s => new GazePoint
        {
            F = s.Features,
            X = s.TargetX,
            Y = s.TargetY,
            FaceY = s.FaceY,
            FaceHeight = s.FaceHeight,
            FromGame = s.FromGame,
        }).ToList();
        // Ошибку считаем по калибровочным точкам: они точно размечены
        calibration.Error = baseline.Average(s =>
        {
            var (x, y) = Predict(calibration, s.Features);
            return Math.Sqrt((x - s.TargetX) * (x - s.TargetX) + (y - s.TargetY) * (y - s.TargetY));
        });
        return calibration;
    }

    /// <summary>
    /// Пересчитать модель с добавленными игровыми точками (игрок верно нажал букву цели —
    /// значит, смотрел на неё). Калибровочные точки сохраняются, игровые — последние
    /// <see cref="MaxGameSamples"/> с меньшим весом.
    /// </summary>
    public static GazeCalibration? Refit(GazeCalibration current, IEnumerable<GazeSample> newGameSamples)
    {
        if (current.Samples == null)
            return null;
        var stored = current.Samples
            .Where(p => p.F.Length == FeatureCount)
            .Select(p => new GazeSample(p.F, p.X, p.Y, p.FaceY, p.FaceHeight, p.FromGame, p.FromGame ? GameSampleWeight : 1));
        var calibrationPart = stored.Where(s => !s.FromGame).ToList();
        var gamePart = stored.Where(s => s.FromGame)
            .Concat(newGameSamples.Select(s => s with { FromGame = true, Weight = GameSampleWeight }))
            .TakeLast(MaxGameSamples);
        return Fit(calibrationPart.Concat(gamePart).ToList(), current.EyeRate, out _);
    }

    public static (double X, double Y) Predict(GazeCalibration c, double[] features)
    {
        var row = Design(features, c.Means!, c.Stds!);
        return (Dot(row, c.WeightsX!), Dot(row, c.WeightsY!));
    }

    static double[] Design(double[] features, double[] means, double[] stds)
    {
        var row = new double[FeatureCount + 1];
        row[0] = 1;
        for (int j = 0; j < FeatureCount; j++)
            row[j + 1] = (features[j] - means[j]) / stds[j];
        return row;
    }

    static double Dot(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += a[i] * b[i];
        return sum;
    }

    /// <summary>Решает (XᵀWX + λI)w = XᵀWy методом Гаусса (свободный член не штрафуется).</summary>
    static double[] Ridge(double[][] x, double[] y, double[] weight)
    {
        int n = x[0].Length;
        var a = new double[n, n + 1];
        for (int r = 0; r < x.Length; r++)
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                    a[i, j] += weight[r] * x[r][i] * x[r][j];
                a[i, n] += weight[r] * x[r][i] * y[r];
            }
        double total = weight.Sum();
        for (int i = 1; i < n; i++)
            a[i, i] += Lambda * total;

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                    pivot = r;
            for (int k = 0; k <= n; k++)
                (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);

            double div = a[col, col];
            if (Math.Abs(div) < 1e-12)
                continue;
            for (int k = col; k <= n; k++)
                a[col, k] /= div;
            for (int r = 0; r < n; r++)
            {
                if (r == col || a[r, col] == 0)
                    continue;
                double factor = a[r, col];
                for (int k = col; k <= n; k++)
                    a[r, k] -= factor * a[col, k];
            }
        }

        var w = new double[n];
        for (int i = 0; i < n; i++)
            w[i] = a[i, n];
        return w;
    }
}
