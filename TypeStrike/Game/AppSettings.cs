using System.IO;
using System.Text.Json;

namespace TypeStrike.Game;

/// <summary>Сохранённая калибровка камеры: положение лица, когда игрок смотрит на экран.</summary>
public sealed class GazeCalibration
{
    public double FaceY { get; set; }
    public double FaceHeight { get; set; }
    public double EyeRate { get; set; }
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
    /// <summary>Учитывать пропажу открытых глаз (веки прикрываются при взгляде вниз).</summary>
    public bool CameraUseEyes { get; set; } = true;
    public bool CalibrateEachGame { get; set; } = true;
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
