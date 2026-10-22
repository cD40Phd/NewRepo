using System.IO;
using System.Text.Json;

namespace TypeStrike.Game;

/// <summary>
/// Сохранённая калибровка камеры: положение лица при взгляде на экран
/// и линейная модель «признаки глаз и лица → точка на экране».
/// </summary>
public sealed class GazeCalibration
{
    public double FaceY { get; set; }
    public double FaceHeight { get; set; }
    public double EyeRate { get; set; }
    /// <summary>Каким набором признаков построена модель (см. GazeModel.FeatureVersion).</summary>
    public int FeatureVersion { get; set; }

    /// <summary>Средние и разброс признаков — для нормировки.</summary>
    public double[]? Means { get; set; }
    public double[]? Stds { get; set; }
    /// <summary>Веса модели для X и Y экрана (первый — свободный член).</summary>
    public double[]? WeightsX { get; set; }
    public double[]? WeightsY { get; set; }
    /// <summary>Средняя ошибка на калибровочных точках в долях экрана.</summary>
    public double Error { get; set; }

    /// <summary>Точки, по которым построена модель: калибровочные и набранные в игре.</summary>
    public List<GazePoint>? Samples { get; set; }

    public int GameSamples => Samples?.Count(s => s.FromGame) ?? 0;

    public bool HasModel(int featureCount) =>
        FeatureVersion == Camera.GazeModel.FeatureVersion &&
        Means?.Length == featureCount && Stds?.Length == featureCount &&
        WeightsX?.Length == featureCount + 1 && WeightsY?.Length == featureCount + 1;
}

/// <summary>Сохранённая точка калибровки: признаки взгляда и куда игрок смотрел.</summary>
public sealed class GazePoint
{
    public double[] F { get; set; } = Array.Empty<double>();
    public double X { get; set; }
    public double Y { get; set; }
    public double FaceY { get; set; }
    public double FaceHeight { get; set; }
    /// <summary>Точка получена в игре (верное нажатие по цели), а не при калибровке.</summary>
    public bool FromGame { get; set; }
}

/// <summary>Настройки игры в %AppData%\TypeStrike\settings.json.</summary>
public sealed class AppSettings
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TypeStrike", "settings.json");

    // По умолчанию: русский язык и уроки слепой печати
    public string Language { get; set; } = "ru";
    public bool LessonsMode { get; set; } = true;
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;

    /// <summary>Масштаб букв: 1 — обычные, 1.4 — крупные, 1.8 — очень крупные.</summary>
    public double TextScale { get; set; } = 1.4;
    public bool Sound { get; set; } = true;
    public bool KeyboardHint { get; set; } = true;

    public bool CameraEnabled { get; set; }
    public int CameraIndex { get; set; }
    /// <summary>Порог наклона головы (в высотах лица), выше которого считаем, что игрок смотрит вниз.</summary>
    public double CameraSensitivity { get; set; } = 0.22;
    /// <summary>Показывать в игре кружок там, куда направлена голова.</summary>
    public bool ShowHeadCursor { get; set; } = true;
    public bool CalibrateEachGame { get; set; }
    /// <summary>Медленно прятать подсказку клавиатуры, пока взгляд следит за атакуемым врагом.</summary>
    public bool AutoHideHint { get; set; } = true;
    /// <summary>Подстраивать модель взгляда во время игры по верным нажатиям.</summary>
    public bool AdaptiveGaze { get; set; } = true;
    public GazeCalibration? Calibration { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // повреждённый файл — настройки по умолчанию
        }
        return new AppSettings();
    }

    public void Save()
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
