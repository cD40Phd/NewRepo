using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TypeStrike.Camera;
using TypeStrike.Game;

namespace TypeStrike;

public partial class MainWindow : Window
{
    enum Screen { Menu, Settings, Calibrating, Game, Paused, GazePaused, GameOver }

    /// <summary>С какой волны отведённый от экрана взгляд ставит игру на паузу.</summary>
    const int GazePauseFromWave = 2;
    /// <summary>За сколько секунд подсказка полностью уезжает вниз при уверенном слежении за целью.</summary>
    const double HintLowerSeconds = 12;
    /// <summary>Сколько секунд взгляд должен удерживаться на цели, прежде чем подсказка начнёт уезжать.</summary>
    const double GazeHoldSeconds = 1.5;
    /// <summary>Сколько секунд взгляд должен продержаться на экране, чтобы снять паузу.</summary>
    const double GazeResumeSeconds = 0.4;
    /// <summary>Через сколько новых игровых точек пересчитывать модель взгляда.</summary>
    const int RefitEvery = 20;
    /// <summary>Игровую точку отбрасываем, если модель промахнулась дальше этого (доля экрана) — игрок смотрел не на цель.</summary>
    const double MaxGameSampleError = 0.45;

    const double MinHeadDrop = 0.10;
    const double MaxHeadDrop = 0.40;

    readonly GameEngine _engine = new();
    readonly SoundEngine _sound = new();
    readonly ScoreStore _scores = ScoreStore.Load();
    readonly AppSettings _settings = AppSettings.Load();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _previousTime;
    Screen _screen = Screen.Menu;
    WordBank? _customBank;
    RadioButton _lastLanguage;
    DateTime _gameOverShownAt;
    LessonPlan? _currentPlan;
    GazeTracker? _gaze;
    bool _loadingSettings;
    DateTime _calibrationSavedAt;
    CalibrationRoutine? _calibration;
    readonly List<GazeSample> _calibrationSamples = new();
    int _calibrationFaceFrames;
    int _calibrationEyeFrames;
    bool _calibrationFailed;
    Action? _afterCalibration;
    Action? _cancelCalibration;
    DispatcherTimer? _gazeResumeTimer;
    double _hintReveal = 1;
    GazeFrame? _lastGazeFrame;
    DateTime _lastGazeFrameAt;
    readonly List<GazeSample> _pendingGameSamples = new();
    bool _calibrationDirty;
    int _adaptedThisGame;
    bool _gazeOnTarget;
    DateTime _gazeOnTargetSince;

    public MainWindow()
    {
        InitializeComponent();
        _lastLanguage = LangRu;
        VersionText.Text = $"версия {AppVersion}";
        Title = $"TypeStrike {AppVersion} — космический тренажёр слепой печати";
        ApplySettingsToControls();

        View.Engine = _engine;
        Hints.Engine = _engine;
        _engine.PlaySound = _sound.Play;
        _engine.GameOver += OnGameOver;
        _engine.WaveCompleted += OnWaveCompleted;
        _engine.TargetKeyHit += OnTargetKeyHit;

        CompositionTarget.Rendering += OnFrame;
        // Ввод с клавиатуры приходит только при наличии фокуса — держим его на игровом поле
        Loaded += (_, _) => Keyboard.Focus(View);
        Activated += (_, _) => Keyboard.Focus(View);
        Deactivated += (_, _) =>
        {
            if (_screen == Screen.Game)
                Pause();
        };
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnFrame;
            SaveAdaptedCalibration();
            StopCamera();
            _sound.Dispose();
        };

        UpdateMenuInfo();
    }

    void OnFrame(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _previousTime, 0.05);
        _previousTime = now;

        if (_screen is not (Screen.Paused or Screen.GazePaused or Screen.Calibrating))
            _engine.Update(dt);
        if (_calibration != null)
            UpdateCalibration(dt);
        UpdateHintReveal(dt);
        if (_gaze != null)
            _gaze.PreviewEnabled = _screen == Screen.Settings || CameraPanel.Visibility == Visibility.Visible;
        View.InvalidateVisual();
        if (HintsHost.Visibility == Visibility.Visible && HintsHost.Height > 0.5)
            Hints.InvalidateVisual();
    }

    // ───────────────────────────── Клавиатура ─────────────────────────────

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (_screen)
        {
            case Screen.Menu:
                if (e.Key == Key.Enter)
                {
                    StartGame();
                    e.Handled = true;
                }
                else if (e.Key == Key.F2)
                {
                    ShowSettings();
                    e.Handled = true;
                }
                break;

            case Screen.Settings:
                if (e.Key is Key.Escape or Key.F2)
                {
                    CloseSettings();
                    e.Handled = true;
                }
                break;

            case Screen.Calibrating:
                e.Handled = true;
                if (e.Key == Key.Escape)
                    CancelCalibration();
                else if (e.Key == Key.Enter && _calibrationFailed)
                    RestartCalibration();
                break;

            case Screen.GazePaused:
                e.Handled = true;
                if (e.Key == Key.Escape)
                {
                    HideGazePause();
                    Pause();
                }
                break;

            case Screen.Game:
                switch (e.Key)
                {
                    case Key.Escape:
                        Pause();
                        e.Handled = true;
                        break;
                    case Key.Enter:
                        _engine.TriggerEmp();
                        e.Handled = true;
                        break;
                    case Key.Back:
                        _engine.ReleaseTarget();
                        e.Handled = true;
                        break;
                    case Key.F5:
                        if (_gaze != null)
                        {
                            Pause();
                            CalibrateFromPause();
                        }
                        e.Handled = true;
                        break;
                    case Key.Space:
                    case Key.Tab:
                        e.Handled = true;
                        break;
                }
                break;

            case Screen.Paused:
                if (e.Key is Key.Escape or Key.Enter)
                {
                    Resume();
                    e.Handled = true;
                }
                else if (e.Key == Key.F5 && _gaze != null)
                {
                    CalibrateFromPause();
                    e.Handled = true;
                }
                break;

            case Screen.GameOver:
                e.Handled = true;
                // Небольшая задержка, чтобы случайно не нажать Enter сразу после гибели
                if ((DateTime.Now - _gameOverShownAt).TotalSeconds < 0.8)
                    break;
                if (e.Key == Key.Enter)
                {
                    if (NextLessonButton.Visibility == Visibility.Visible)
                        StartNextLesson();
                    else if (ContinueOverButton.Visibility == Visibility.Visible)
                        StartGame(resume: true);
                    else
                        StartGame();
                }
                else if (e.Key == Key.Escape)
                    ShowMenu();
                break;
        }
    }

    void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_screen != Screen.Game)
            return;

        foreach (char ch in e.Text)
            if (char.IsLetter(ch))
                _engine.OnChar(ch);
        e.Handled = true;
    }

    // ───────────────────────────── Экраны ─────────────────────────────

    /// <param name="resume">Продолжить свободную игру с сохранённой волны.</param>
    void StartGame(bool resume = false)
    {
        bool needsCalibration = _settings.CameraEnabled &&
            (_settings.CalibrateEachGame || _settings.Calibration?.HasModel(GazeModel.FeatureCount) != true);
        if (needsCalibration)
        {
            HideAllPanels();
            BeginCalibration(() => LaunchGame(resume), ShowMenu);
        }
        else
        {
            LaunchGame(resume);
        }
    }

    void HideAllPanels()
    {
        MenuPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        PausePanel.Visibility = Visibility.Collapsed;
        GameOverPanel.Visibility = Visibility.Collapsed;
        GazePausePanel.Visibility = Visibility.Collapsed;
    }

    void LaunchGame(bool resume)
    {
        var bank = SelectedBank();
        var difficulty = SelectedDifficulty();
        _sound.Enabled = _settings.Sound;
        TrySwitchKeyboardLayout(bank.KeyboardCulture);

        var lesson = SelectedLesson();
        _currentPlan = lesson != null ? LessonCatalog.CreatePlan(bank, lesson) : null;
        var checkpoint = resume && _currentPlan == null ? _scores.GetCheckpoint(ScoreKey(bank, difficulty)) : null;
        _engine.Start(bank, difficulty, _currentPlan, checkpoint?.Wave ?? 0, checkpoint?.Score ?? 0);

        HintsHost.Visibility = _settings.KeyboardHint ? Visibility.Visible : Visibility.Collapsed;
        _adaptedThisGame = 0;
        _pendingGameSamples.Clear();
        _hintReveal = 1;
        _gazeOnTarget = false;
        UpdateHintReveal(0);
        if (_settings.CameraEnabled)
        {
            StartCamera();
            CameraPanel.Visibility = Visibility.Visible;
        }
        else
        {
            StopCamera();
        }
        MenuPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        PausePanel.Visibility = Visibility.Collapsed;
        GameOverPanel.Visibility = Visibility.Collapsed;
        _screen = Screen.Game;
        Keyboard.Focus(View);
    }

    void Pause()
    {
        HideGazePause();
        _screen = Screen.Paused;
        PauseCalibrateButton.Visibility = _gaze != null ? Visibility.Visible : Visibility.Collapsed;
        PausePanel.Visibility = Visibility.Visible;
    }

    void Resume()
    {
        PausePanel.Visibility = Visibility.Collapsed;
        _screen = Screen.Game;
        Keyboard.Focus(View);
    }

    void ShowMenu()
    {
        SaveAdaptedCalibration();
        _engine.Reset();
        HideGazePause();
        StopCamera();
        HintsHost.Visibility = Visibility.Collapsed;
        PausePanel.Visibility = Visibility.Collapsed;
        GameOverPanel.Visibility = Visibility.Collapsed;
        MenuPanel.Visibility = Visibility.Visible;
        _screen = Screen.Menu;
        UpdateMenuInfo();
    }

    void OnGameOver(GameResult result)
    {
        SaveAdaptedCalibration();
        bool record = _scores.Submit(ScoreKey(_engine.Words, _engine.Difficulty), result);
        NewRecordText.Visibility = record ? Visibility.Visible : Visibility.Collapsed;
        string lessonMessage = ShowLessonOutcome(result);

        StatsGrid.Children.Clear();
        StatsGrid.RowDefinitions.Clear();
        if (result.Lesson != null)
        {
            // В уроке главное — точность; скорость показываем, но не оцениваем
            AddStat("Точность", $"{result.Accuracy:0.#}%  (нужно {LessonCatalog.PassAccuracy:0}–100%)",
                result.Accuracy >= LessonCatalog.PassAccuracy ? "#6BFFB0" : "#FF9F1C");
            AddStat("Скорость", $"{result.Wpm:0} WPM  ({result.Wpm * 5:0} зн/мин)");
            AddStat("Столкновения", result.Collisions.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            AddStat("Счёт", result.Score.ToString("N0", CultureInfo.CurrentCulture), "#FF9F1C");
            AddStat("Волна", result.Wave.ToString(CultureInfo.InvariantCulture));
            AddStat("Скорость", $"{result.Wpm:0} WPM  ({result.Wpm * 5:0} зн/мин)");
            AddStat("Точность", $"{result.Accuracy:0.#}%");
            AddStat("Макс. комбо", result.MaxCombo.ToString(CultureInfo.InvariantCulture));
            AddStat("Сбито кораблей", result.Words.ToString(CultureInfo.InvariantCulture));
        }
        if (_adaptedThisGame > 0)
            AddStat("Подстройка взгляда", $"+{_adaptedThisGame} точек", "#5CE1FF");
        if (_gaze != null)
            AddStat("Взгляд на клавиатуру", result.Peeks == 0 ? "ни разу 👍" : $"{result.Peeks} раз",
                result.Peeks == 0 ? "#6BFFB0" : "#FF9F1C");

        var checkpoint = result.Lesson == null ? _scores.GetCheckpoint(ScoreKey(_engine.Words, _engine.Difficulty)) : null;
        ContinueOverButton.Visibility = checkpoint != null ? Visibility.Visible : Visibility.Collapsed;
        if (checkpoint != null)
        {
            ContinueOverButton.Content = $"⟳  С ВОЛНЫ {checkpoint.Wave + 1}  [Enter]";
            RestartButton.Content = "НОВАЯ ИГРА";
            lessonMessage = $"Уровень сохранён: пройдено волн — {checkpoint.Wave}. Можно продолжить с волны {checkpoint.Wave + 1}.\n\n";
        }

        TroubleText.Text = lessonMessage
            + (string.IsNullOrEmpty(result.TroubleLetters)
                ? "Ни одной ошибки — отличная работа!"
                : "Чаще всего ошибались на буквах:\n" + result.TroubleLetters)
            + (result.Lesson != null ? "\n\nСовет: " + LessonCatalog.Tips[Random.Shared.Next(LessonCatalog.Tips.Length)] : "");

        GameOverPanel.Visibility = Visibility.Visible;
        _screen = Screen.GameOver;
        _gameOverShownAt = DateTime.Now;
    }

    /// <summary>Заголовок, звёзды и прогресс освоения урока. Возвращает пояснение для итогов.</summary>
    string ShowLessonOutcome(GameResult result)
    {
        NextLessonButton.Visibility = Visibility.Collapsed;
        ResultStars.Visibility = Visibility.Collapsed;

        if (result.Lesson == null || _currentPlan == null)
        {
            SetTitle("КОРАБЛЬ УНИЧТОЖЕН", "#FF4D6D");
            RestartButton.Content = "ИГРАТЬ СНОВА  [Enter]";
            return "";
        }

        var bank = SelectedBank();
        var lessons = LessonCatalog.For(bank);
        bool passed = result.Completed && result.Stars > 0;
        bool wasMastered = _scores.IsMastered(_currentPlan.Key);
        var mastery = _scores.AddAttempt(_currentPlan.Key, new LessonAttempt
        {
            Accuracy = result.Accuracy,
            Wpm = result.Wpm,
            Completed = result.Completed,
            Date = DateTime.Now,
        });
        if (passed)
            _scores.SubmitStars(_currentPlan.Key, result.Stars);

        ResultStars.Visibility = Visibility.Visible;
        ResultStars.Text = new string('★', result.Stars) + new string('☆', 3 - result.Stars);

        bool hasNext = mastery.Mastered && result.Lesson.Number < lessons.Count;
        if (hasNext && !wasMastered)
            _scores.SetCurrentLesson(bank.Id, result.Lesson.Number);
        NextLessonButton.Visibility = hasNext ? Visibility.Visible : Visibility.Collapsed;
        RestartButton.Content = "ПОВТОРИТЬ УРОК" + (hasNext ? "" : "  [Enter]");

        int need = LessonCatalog.MasteryStreak;
        string progress = $"Освоение: {Dots(mastery.Streak)}  ({Math.Min(mastery.Streak, need)} из {need} зачётов подряд)";

        if (mastery.Mastered && !wasMastered)
        {
            SetTitle("УРОК ОСВОЕН", "#6BFFB0");
            return progress + (hasNext ? "\nПальцы запомнили зоны — открыта следующая буква!\n\n" : "\nВсе буквы изучены — переходите к свободной игре!\n\n");
        }
        if (passed)
        {
            SetTitle("ЗАЧЁТ", "#6BFFB0");
            string next = mastery.Mastered
                ? "Урок уже освоен — можно идти дальше или закреплять."
                : mastery.Streak >= need && !mastery.StableSpeed
                    ? "Точность отличная, но скорость пока скачет — печатайте ровно, в одном ритме."
                    : $"Ещё {need - mastery.Streak} стабильн. результат(а) — и добавится новая буква.";
            return progress + "\n" + next + "\n\n";
        }

        SetTitle(result.Completed ? "НУЖНО ТОЧНЕЕ" : "КОРАБЛЬ УНИЧТОЖЕН", "#FF9F1C");
        return progress + $"\nТочность {result.Accuracy:0.#}% — нужно {LessonCatalog.PassAccuracy:0}–100%. " +
               "Не спешите: скорость придёт сама, когда пальцы запомнят свои зоны.\n\n";
    }

    static string Dots(int streak) =>
        new string('●', Math.Min(streak, LessonCatalog.MasteryStreak)) +
        new string('○', Math.Max(0, LessonCatalog.MasteryStreak - streak));

    void SetTitle(string text, string color)
    {
        GameOverTitle.Text = text;
        GameOverTitle.Foreground = (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    void StartNextLesson()
    {
        // Индекс следующего урока уже сохранён в ShowLessonOutcome
        UpdateMenuInfo();
        StartGame();
    }

    void NextLesson_Click(object sender, RoutedEventArgs e) => StartNextLesson();
    void Continue_Click(object sender, RoutedEventArgs e) => StartGame(resume: true);

    /// <summary>Сохраняем уровень свободной игры после каждой пройденной волны.</summary>
    void OnWaveCompleted(int wave, int score)
    {
        if (_engine.Lesson == null)
            _scores.SaveCheckpoint(ScoreKey(_engine.Words, _engine.Difficulty), wave, score);
    }

    void AddStat(string label, string value, string color = "#F2F6FF")
    {
        int row = StatsGrid.RowDefinitions.Count;
        StatsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 17,
            Foreground = (Brush)FindResource("DimBrush"),
            Margin = new Thickness(0, 3, 0, 3),
        };
        var number = new TextBlock
        {
            Text = value,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)new BrushConverter().ConvertFromString(color)!,
            Margin = new Thickness(0, 3, 0, 3),
        };
        Grid.SetRow(name, row);
        Grid.SetRow(number, row);
        Grid.SetColumn(number, 1);
        StatsGrid.Children.Add(name);
        StatsGrid.Children.Add(number);
    }

    // ───────────────────────────── Камера ─────────────────────────────

    double HeadDropThreshold => Math.Clamp(_settings.CameraSensitivity, MinHeadDrop, MaxHeadDrop);

    void StartCamera()
    {
        if (_gaze != null && _gaze.CameraIndex != _settings.CameraIndex)
            StopCamera();
        if (_gaze != null)
            return;

        var tracker = new GazeTracker(Dispatcher, _settings.CameraIndex)
        {
            HeadDropThreshold = HeadDropThreshold,
            UseEyes = _settings.CameraUseEyes,
        };
        // События уже остановленного трекера могут прийти позже — игнорируем их
        tracker.StateChanged += state =>
        {
            if (_gaze == tracker)
                OnGazeStateChanged(state);
        };
        tracker.Preview += bitmap =>
        {
            if (_gaze != tracker)
                return;
            CameraImage.Source = bitmap;
            SettingsCameraImage.Source = bitmap;
        };
        tracker.Frame += frame =>
        {
            if (_gaze == tracker)
                OnGazeFrame(frame);
        };
        _gaze = tracker;
        OnGazeStateChanged(GazeState.Starting);
        tracker.Start(_settings.Calibration);
    }

    void StopCamera()
    {
        _gazeResumeTimer?.Stop();
        _gaze?.Dispose();
        _gaze = null;
        _engine.SetLookingDown(false);
        CameraImage.Source = null;
        SettingsCameraImage.Source = null;
        CameraPanel.Visibility = Visibility.Collapsed;
        SettingsCameraStatus.Text = _settings.CameraEnabled ? "Камера выключена" : "Включите камеру, чтобы настроить её";
        MetricsText.Text = "";
        DropFill.Width = 0;
        GazeDot.Visibility = Visibility.Hidden;
    }

    void OnGazeStateChanged(GazeState state)
    {
        string text = state switch
        {
            GazeState.Starting => "Включаю камеру…",
            GazeState.NeedsCalibration => "Нужна калибровка взгляда",
            GazeState.OnScreen => "✔ Взгляд на экране",
            GazeState.LookingAway => "👀 Взгляд не на экране!",
            GazeState.NoFace => "Лицо не в кадре",
            _ => "Камера недоступна: " + _gaze?.ErrorMessage,
        };
        var brush = (Brush)new BrushConverter().ConvertFromString(state switch
        {
            GazeState.OnScreen => "#6BFFB0",
            GazeState.LookingAway or GazeState.Error or GazeState.NeedsCalibration => "#FF9F1C",
            _ => "#C8D3EA",
        })!;
        CameraStatus.Text = SettingsCameraStatus.Text = text;
        CameraStatus.Foreground = SettingsCameraStatus.Foreground = brush;

        bool away = state == GazeState.LookingAway;
        _engine.SetLookingDown(away);

        // Со второй волны взгляд мимо экрана ставит игру на паузу
        if (away && _screen == Screen.Game && _engine.Phase == Phase.Fighting && _engine.Wave >= GazePauseFromWave)
            ShowGazePause();
        else if (state == GazeState.OnScreen && _screen == Screen.GazePaused)
            ScheduleGazeResume();
        else if (_screen == Screen.GazePaused)
        {
            _gazeResumeTimer?.Stop();
            GazeResumeText.Text = "";
        }
    }

    void OnGazeFrame(GazeFrame frame)
    {
        _lastGazeFrame = frame;
        _lastGazeFrameAt = DateTime.Now;

        if (_calibration is { Collecting: true } routine && !_calibrationFailed)
        {
            if (frame.Face)
            {
                _calibrationFaceFrames++;
                if (frame.Eyes)
                    _calibrationEyeFrames++;
            }
            if (frame.Features != null)
            {
                var (x, y) = routine.Target;
                _calibrationSamples.Add(new GazeSample(frame.Features, x, y, frame.FaceY, frame.FaceHeight));
            }
        }

        UpdateGazeOnTarget(frame);

        if (_screen != Screen.Settings)
            return;

        double width = DropBar.ActualWidth;
        DropFill.Width = Math.Clamp(frame.HeadDrop / 0.5, 0, 1) * width;
        DropMarker.Margin = new Thickness(Math.Clamp(HeadDropThreshold / 0.5, 0, 1) * width, 0, 0, 0);
        DropFill.Fill = (Brush)new BrushConverter().ConvertFromString(frame.HeadDrop > HeadDropThreshold ? "#FF9F1C" : "#5CE1FF")!;

        string gaze = frame.HasGaze ? $"взгляд ({frame.GazeX:0.00}; {frame.GazeY:0.00})" : "взгляд: —";
        MetricsText.Text = $"наклон {Math.Max(0, frame.HeadDrop):0.00} · порог {HeadDropThreshold:0.00}\n" +
                           $"лицо: {(frame.Face ? "видно" : "не видно")} · глаза: {(frame.Eyes ? "видны" : "не видны")}\n{gaze}" +
                           $"\nкамера: {frame.Fps:0} к/с · обработка {frame.ProcessingMs:0} мс · задержка {frame.LatencyMs:0} мс" +
                           (_settings.Calibration is { } c && c.HasModel(GazeModel.FeatureCount)
                               ? $"\nкалибровка: ошибка ≈ {c.Error * 100:0}% · подстройка в игре: {c.GameSamples} точек"
                               : "") +
                           ((DateTime.Now - _calibrationSavedAt).TotalSeconds < 5 ? "\n✔ калибровка сохранена" : "");

        // Схема: рамка экрана 140×80 со сдвигом (30; 15) на холсте 200×130
        if (frame.HasGaze)
        {
            double gx = 30 + Math.Clamp(frame.GazeX, -0.2, 1.2) * 140;
            double gy = 15 + Math.Clamp(frame.GazeY, -0.18, 1.35) * 80;
            Canvas.SetLeft(GazeDot, gx - GazeDot.Width / 2);
            Canvas.SetTop(GazeDot, gy - GazeDot.Height / 2);
            GazeDot.Fill = (Brush)new BrushConverter().ConvertFromString(
                GazeTracker.IsOffScreen(frame.GazeX, frame.GazeY) ? "#FF4D6D" : "#FF9F1C")!;
            GazeDot.Visibility = Visibility.Visible;
        }
        else
        {
            GazeDot.Visibility = Visibility.Hidden;
        }
    }

    // ───────────────────────────── Взгляд на цели и подсказка ─────────────────────────────

    /// <summary>
    /// Смотрит ли игрок туда, где находится атакуемый враг (корабль и подпись под ним).
    /// Допуск зависит от точности калибровки; с гистерезисом, чтобы не мигало.
    /// </summary>
    void UpdateGazeOnTarget(GazeFrame frame)
    {
        bool onTarget = false;
        var target = _engine.HintEnemy;
        double w = Root.ActualWidth, h = Root.ActualHeight;
        if (_screen == Screen.Game && frame.HasGaze && target != null && w > 0 && h > 0)
        {
            var (tx, ty) = TargetPoint(target);
            double tolerance = Math.Max(0.12, (_settings.Calibration?.Error ?? 0.1) * 1.6) * (_gazeOnTarget ? 1.3 : 1.0);
            double dx = frame.GazeX - tx, dy = frame.GazeY - ty;
            onTarget = dx * dx + dy * dy <= tolerance * tolerance;
        }

        if (onTarget && !_gazeOnTarget)
            _gazeOnTargetSince = DateTime.Now;
        _gazeOnTarget = onTarget;
        _engine.GazeOnTarget = onTarget;
    }

    /// <summary>Куда смотрит игрок, атакуя врага: между кораблём и подписью под ним, в долях окна.</summary>
    (double X, double Y) TargetPoint(Enemy target) =>
        (target.Pos.X / Root.ActualWidth, (target.Pos.Y + target.Radius + 12) / Root.ActualHeight);

    /// <summary>
    /// Подстройка калибровки в игре: при верном нажатии буквы цели игрок смотрит на цель,
    /// так что признаки глаз в этот момент + положение цели — ещё одна точка калибровки.
    /// </summary>
    void OnTargetKeyHit(Enemy target)
    {
        var calibration = _settings.Calibration;
        var frame = _lastGazeFrame;
        if (!_settings.AdaptiveGaze || _gaze?.State != GazeState.OnScreen || frame?.Features == null ||
            calibration?.HasModel(GazeModel.FeatureCount) != true || calibration.Samples == null ||
            (DateTime.Now - _lastGazeFrameAt).TotalSeconds > 0.25 || Root.ActualWidth <= 0 || Root.ActualHeight <= 0)
            return;

        var (tx, ty) = TargetPoint(target);
        // Модель промахнулась слишком сильно — вероятно, игрок смотрел не на цель (или кадр плохой)
        if (frame.HasGaze && Math.Sqrt(Math.Pow(frame.GazeX - tx, 2) + Math.Pow(frame.GazeY - ty, 2)) > MaxGameSampleError)
            return;

        _pendingGameSamples.Add(new GazeSample(frame.Features, tx, ty, frame.FaceY, frame.FaceHeight,
            FromGame: true, Weight: GazeModel.GameSampleWeight));
        if (_pendingGameSamples.Count >= RefitEvery)
            ApplyGameSamples();
    }

    void ApplyGameSamples()
    {
        var calibration = _settings.Calibration;
        if (_pendingGameSamples.Count == 0 || calibration == null)
            return;
        var updated = GazeModel.Refit(calibration, _pendingGameSamples);
        _adaptedThisGame += _pendingGameSamples.Count;
        _pendingGameSamples.Clear();
        if (updated == null)
            return;
        _settings.Calibration = updated;
        _gaze?.SetCalibration(updated);
        _calibrationDirty = true;
    }

    /// <summary>Сохраняем подстроенную калибровку не на каждое нажатие, а в конце игры.</summary>
    void SaveAdaptedCalibration()
    {
        if (_pendingGameSamples.Count >= 5)
            ApplyGameSamples();
        _pendingGameSamples.Clear();
        if (_calibrationDirty)
        {
            _settings.Save();
            _calibrationDirty = false;
        }
    }

    bool GazeTrackingActive =>
        _gaze?.State is GazeState.OnScreen or GazeState.LookingAway &&
        _settings.Calibration?.HasModel(GazeModel.FeatureCount) == true;

    /// <summary>
    /// Пока взгляд уверенно следит за целью, подсказка медленно уезжает вниз.
    /// Новая буква, ошибка или взгляд мимо экрана быстро возвращают её.
    /// </summary>
    void UpdateHintReveal(double dt)
    {
        if (HintsHost.Visibility != Visibility.Visible)
            return;

        bool autoHide = _settings.AutoHideHint && GazeTrackingActive;
        if (!autoHide)
        {
            _hintReveal = Math.Min(1, _hintReveal + dt * 3);
        }
        else if (_screen == Screen.Game && _engine.Phase == Phase.Fighting)
        {
            bool needed = _engine.HintReason() != null || _gaze?.State == GazeState.LookingAway;
            bool tracking = _gazeOnTarget && (DateTime.Now - _gazeOnTargetSince).TotalSeconds >= GazeHoldSeconds;
            if (needed)
                _hintReveal = Math.Min(1, _hintReveal + dt * 3);
            else if (tracking)
                _hintReveal = Math.Max(0, _hintReveal - dt / HintLowerSeconds);
        }

        _engine.HintLowered = autoHide && _hintReveal < 0.15;
        HintsHost.Height = Hints.Height * _hintReveal;
    }

    // ───────────────────────────── Пауза по взгляду ─────────────────────────────

    void ShowGazePause()
    {
        _screen = Screen.GazePaused;
        GazeResumeText.Text = "";
        GazePausePanel.Visibility = Visibility.Visible;
        ((System.Windows.Media.Animation.Storyboard)FindResource("GazeWarnStoryboard")).Begin(this, true);
        _sound.Play(Sfx.Miss);
    }

    void HideGazePause()
    {
        _gazeResumeTimer?.Stop();
        ((System.Windows.Media.Animation.Storyboard)FindResource("GazeWarnStoryboard")).Stop(this);
        GazePausePanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>Взгляд вернулся — продолжаем через секунду, если он так и останется на экране.</summary>
    void ScheduleGazeResume()
    {
        GazeResumeText.Text = "Отлично! Продолжаем…";
        _gazeResumeTimer?.Stop();
        _gazeResumeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GazeResumeSeconds) };
        _gazeResumeTimer.Tick += (_, _) =>
        {
            _gazeResumeTimer?.Stop();
            if (_screen != Screen.GazePaused || _gaze?.State != GazeState.OnScreen)
                return;
            HideGazePause();
            _screen = Screen.Game;
            Keyboard.Focus(View);
        };
        _gazeResumeTimer.Start();
    }

    // ───────────────────────────── Калибровка взгляда ─────────────────────────────

    /// <summary>Калибровка по движущейся точке: игрок следит за ней глазами ~20 секунд.</summary>
    void BeginCalibration(Action onSuccess, Action onCancel)
    {
        StartCamera();
        _afterCalibration = onSuccess;
        _cancelCalibration = onCancel;
        CameraPanel.Visibility = Visibility.Collapsed;
        CalibrationLayer.Visibility = Visibility.Visible;
        _screen = Screen.Calibrating;
        RestartCalibration();
    }

    void RestartCalibration()
    {
        _calibration = new CalibrationRoutine();
        _calibrationSamples.Clear();
        _calibrationFaceFrames = _calibrationEyeFrames = 0;
        _calibrationFailed = false;
        CalibDot.Visibility = CalibGlow.Visibility = CalibCenter.Visibility = Visibility.Visible;
        UpdateCalibration(0);
    }

    void UpdateCalibration(double dt)
    {
        var routine = _calibration!;
        if (_calibrationFailed)
            return;
        routine.Update(dt);

        double w = CalibCanvas.ActualWidth, h = CalibCanvas.ActualHeight;
        var (tx, ty) = routine.Target;
        double pulse = routine.Dwelling ? 0.75 + 0.25 * Math.Cos(routine.Time * 9) : 1;
        double dot = 34 * pulse;
        Place(CalibGlow, tx * w, ty * h, 90);
        Place(CalibDot, tx * w, ty * h, dot);
        Place(CalibCenter, tx * w, ty * h, 8);
        CalibProgress.Width = routine.Progress * ActualWidth;

        CalibTitle.Text = routine.IsIntro ? $"КАЛИБРОВКА ВЗГЛЯДА · {routine.IntroCountdown}" : "СЛЕДИТЕ ЗА ТОЧКОЙ";
        CalibInfo.Text = routine.IsIntro
            ? "Сядьте как при игре, пальцы на основном ряду. Следите глазами за оранжевой точкой, голову держите ровно. Esc — отмена."
            : _gaze?.State == GazeState.Error
                ? "Камера недоступна: " + _gaze.ErrorMessage
                : $"Кадров записано: {_calibrationSamples.Count}";

        if (routine.IsDone)
            FinishCalibration();
    }

    static void Place(FrameworkElement element, double x, double y, double size)
    {
        element.Width = element.Height = size;
        Canvas.SetLeft(element, x - size / 2);
        Canvas.SetTop(element, y - size / 2);
    }

    void FinishCalibration()
    {
        double eyeRate = _calibrationFaceFrames == 0 ? 0 : (double)_calibrationEyeFrames / _calibrationFaceFrames;
        var calibration = GazeModel.Fit(_calibrationSamples, eyeRate, out var error);
        if (calibration == null)
        {
            _calibrationFailed = true;
            CalibDot.Visibility = CalibGlow.Visibility = CalibCenter.Visibility = Visibility.Hidden;
            CalibTitle.Text = "НЕ УДАЛОСЬ ОТКАЛИБРОВАТЬ";
            CalibInfo.Text = error + "\n\nEnter — повторить, Esc — отмена";
            return;
        }

        _settings.Calibration = calibration;
        _settings.Save();
        _calibrationSavedAt = DateTime.Now;
        _gaze?.SetCalibration(calibration);

        _calibration = null;
        CalibDot.Visibility = CalibGlow.Visibility = CalibCenter.Visibility = Visibility.Hidden;
        CalibTitle.Text = "ГОТОВО!";
        CalibInfo.Text = $"Средняя ошибка ≈ {calibration.Error * 100:0}% экрана" +
                         (calibration.Error > 0.2 ? " — неточно: попробуйте при лучшем свете и не двигая головой" : "");

        var done = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        done.Tick += (_, _) =>
        {
            done.Stop();
            CloseCalibrationLayer();
            var next = _afterCalibration;
            _afterCalibration = _cancelCalibration = null;
            next?.Invoke();
        };
        done.Start();
    }

    void CancelCalibration()
    {
        CloseCalibrationLayer();
        var cancel = _cancelCalibration;
        _afterCalibration = _cancelCalibration = null;
        cancel?.Invoke();
    }

    void CloseCalibrationLayer()
    {
        _calibration = null;
        _calibrationFailed = false;
        CalibrationLayer.Visibility = Visibility.Collapsed;
    }

    /// <summary>Калибровка из паузы (F5) — после неё возвращаемся на паузу.</summary>
    void CalibrateFromPause()
    {
        PausePanel.Visibility = Visibility.Collapsed;
        BeginCalibration(Pause, Pause);
    }

    void PauseCalibrate_Click(object sender, RoutedEventArgs e) => CalibrateFromPause();

    // ───────────────────────────── Настройки ─────────────────────────────

    void ApplySettingsToControls()
    {
        _loadingSettings = true;

        var language = _settings.Language == "en" ? LangEn : LangRu;
        language.IsChecked = true;
        _lastLanguage = language;
        (_settings.LessonsMode ? ModeLessons : ModeFree).IsChecked = true;
        (_settings.Difficulty switch { Difficulty.Easy => DiffEasy, Difficulty.Hard => DiffHard, _ => DiffNormal }).IsChecked = true;

        (_settings.TextScale < 1.2 ? SizeNormal : _settings.TextScale < 1.6 ? SizeLarge : SizeHuge).IsChecked = true;
        HintsBox.IsChecked = _settings.KeyboardHint;
        SoundBox.IsChecked = _settings.Sound;
        CameraBox.IsChecked = _settings.CameraEnabled;
        (_settings.CameraIndex switch { 1 => Cam2, 2 => Cam3, _ => Cam1 }).IsChecked = true;
        SensitivitySlider.Value = (MaxHeadDrop - HeadDropThreshold) / (MaxHeadDrop - MinHeadDrop) * 100;
        UseEyesBox.IsChecked = _settings.CameraUseEyes;
        CalibrateEachGameBox.IsChecked = _settings.CalibrateEachGame;
        AutoHideHintBox.IsChecked = _settings.AutoHideHint;
        AdaptiveGazeBox.IsChecked = _settings.AdaptiveGaze;

        ApplyTextScale();
        UpdateSensitivityText();
        CameraSettings.IsEnabled = _settings.CameraEnabled;
        _loadingSettings = false;
    }

    void ApplyTextScale()
    {
        double scale = _settings.TextScale;
        View.TextScale = scale;
        Hints.TextScale = scale;
        Hints.Height = (224 + 32 * scale) * 0.9;
        HintsHost.Height = Hints.Height * _hintReveal;
        SizePreview.FontSize = 16 * scale;
    }

    void UpdateSensitivityText()
    {
        SensitivityText.Text = $"Чувствительность: {SensitivitySlider.Value:0}%  (порог наклона головы {HeadDropThreshold:0.00})";
    }

    void ShowSettings()
    {
        MenuPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        _screen = Screen.Settings;
        if (_settings.CameraEnabled)
            StartCamera();
        else
            StopCamera();
    }

    void CloseSettings()
    {
        StopCamera();
        SettingsPanel.Visibility = Visibility.Collapsed;
        MenuPanel.Visibility = Visibility.Visible;
        _screen = Screen.Menu;
        UpdateMenuInfo();
    }

    void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();
    void SettingsDone_Click(object sender, RoutedEventArgs e) => CloseSettings();

    void TextSize_Click(object sender, RoutedEventArgs e)
    {
        _settings.TextScale = SizeNormal.IsChecked == true ? 1.0 : SizeHuge.IsChecked == true ? 1.8 : 1.4;
        _settings.Save();
        ApplyTextScale();
    }

    void SettingsChanged(object sender, RoutedEventArgs e)
    {
        _settings.KeyboardHint = HintsBox.IsChecked == true;
        _settings.Sound = SoundBox.IsChecked == true;
        _settings.Save();
    }

    void CameraBox_Click(object sender, RoutedEventArgs e)
    {
        _settings.CameraEnabled = CameraBox.IsChecked == true;
        _settings.Save();
        CameraSettings.IsEnabled = _settings.CameraEnabled;
        if (_settings.CameraEnabled)
            StartCamera();
        else
            StopCamera();
    }

    void CameraIndex_Click(object sender, RoutedEventArgs e)
    {
        _settings.CameraIndex = Cam2.IsChecked == true ? 1 : Cam3.IsChecked == true ? 2 : 0;
        // Другая камера — другое положение лица в кадре, старая калибровка не подходит
        _settings.Calibration = null;
        _settings.Save();
        StopCamera();
        if (_settings.CameraEnabled)
            StartCamera();
    }

    void Sensitivity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loadingSettings || !IsInitialized)
            return;
        _settings.CameraSensitivity = MaxHeadDrop - SensitivitySlider.Value / 100 * (MaxHeadDrop - MinHeadDrop);
        _settings.Save();
        if (_gaze != null)
            _gaze.HeadDropThreshold = HeadDropThreshold;
        UpdateSensitivityText();
    }

    void CameraOption_Click(object sender, RoutedEventArgs e)
    {
        _settings.CameraUseEyes = UseEyesBox.IsChecked == true;
        _settings.CalibrateEachGame = CalibrateEachGameBox.IsChecked == true;
        _settings.AutoHideHint = AutoHideHintBox.IsChecked == true;
        _settings.AdaptiveGaze = AdaptiveGazeBox.IsChecked == true;
        _settings.Save();
        if (_gaze != null)
            _gaze.UseEyes = _settings.CameraUseEyes;
    }

    void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.CameraEnabled)
            return;
        SettingsPanel.Visibility = Visibility.Collapsed;
        BeginCalibration(ReturnToSettings, ReturnToSettings);
    }

    void ReturnToSettings()
    {
        SettingsPanel.Visibility = Visibility.Visible;
        _screen = Screen.Settings;
    }

    // ───────────────────────────── Меню ─────────────────────────────

    void Start_Click(object sender, RoutedEventArgs e) => StartGame();
    void Resume_Click(object sender, RoutedEventArgs e) => Resume();
    void Menu_Click(object sender, RoutedEventArgs e) => ShowMenu();
    void Difficulty_Click(object sender, RoutedEventArgs e) => UpdateMenuInfo();
    void Mode_Click(object sender, RoutedEventArgs e) => UpdateMenuInfo();

    void LessonPrev_Click(object sender, RoutedEventArgs e) => ShiftLesson(-1);
    void LessonNext_Click(object sender, RoutedEventArgs e) => ShiftLesson(+1);

    void ShiftLesson(int delta)
    {
        var bank = SelectedBank();
        var lessons = LessonCatalog.For(bank);
        if (lessons.Count == 0)
            return;
        // Новая буква открывается только после освоения предыдущего урока
        int index = Math.Clamp(_scores.GetCurrentLesson(bank.Id) + delta, 0, UnlockedLessons(bank, lessons) - 1);
        _scores.SetCurrentLesson(bank.Id, index);
        UpdateMenuInfo();
    }

    /// <summary>Сколько уроков открыто: все освоенные подряд плюс первый неосвоенный.</summary>
    int UnlockedLessons(WordBank bank, IReadOnlyList<Lesson> lessons)
    {
        int unlocked = 1;
        while (unlocked < lessons.Count && _scores.IsMastered($"{bank.Id}-lesson{lessons[unlocked - 1].Number}"))
            unlocked++;
        return unlocked;
    }

    /// <summary>Выбранный урок или null, если включена свободная игра (или свой файл слов).</summary>
    Lesson? SelectedLesson()
    {
        if (ModeLessons.IsChecked != true)
            return null;
        var bank = SelectedBank();
        var lessons = LessonCatalog.For(bank);
        if (lessons.Count == 0)
            return null;
        return lessons[Math.Clamp(_scores.GetCurrentLesson(bank.Id), 0, UnlockedLessons(bank, lessons) - 1)];
    }

    void Lang_Click(object sender, RoutedEventArgs e)
    {
        _lastLanguage = (RadioButton)sender;
        UpdateMenuInfo();
    }

    void LangCustom_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Файл со словами (через пробел или по одному на строку)",
            Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                var bank = WordBank.FromFile(dialog.FileName);
                if (bank.Count < 10)
                    throw new InvalidDataException($"В файле всего {bank.Count} слов — нужно хотя бы 10.");
                _customBank = bank;
                _lastLanguage = LangCustom;
                CustomInfo.Text = $"{Path.GetFileName(dialog.FileName)} — {bank.Count} слов";
                CustomInfo.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Не удалось загрузить слова", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        if (_customBank == null || _lastLanguage != LangCustom)
            _lastLanguage.IsChecked = true;
        UpdateMenuInfo();
    }

    void UpdateMenuInfo()
    {
        var lesson = SelectedLesson();
        LessonPanel.Visibility = lesson != null ? Visibility.Visible : Visibility.Collapsed;
        if (ModeLessons.IsChecked == true && lesson == null)
        {
            // Для своего файла слов уроков нет
            LessonPanel.Visibility = Visibility.Visible;
            LessonTitle.Text = "Уроки доступны для English и Русского";
            LessonLetters.Text = "для своего файла слов будет свободная игра";
            LessonStars.Text = "";
            LessonTip.Text = "";
        }
        else if (lesson != null)
        {
            var bank = SelectedBank();
            var lessons = LessonCatalog.For(bank);
            string key = $"{bank.Id}-lesson{lesson.Number}";
            int stars = _scores.StarsFor(key);
            var mastery = _scores.GetMastery(key);
            int mastered = lessons.Count(l => _scores.IsMastered($"{bank.Id}-lesson{l.Number}"));
            LessonTitle.Text = $"{lesson.Title}   ({lesson.Number}/{lessons.Count})";
            LessonLetters.Text = $"изученные буквы: {lesson.Letters.ToUpperInvariant()}  ·  освоено уроков: {mastered}";
            LessonStars.Text = new string('★', stars) + new string('☆', 3 - stars) + "    " +
                (mastery.Mastered ? "освоен ✔" : $"освоение {Dots(mastery.Streak)}");
            LessonTip.Text = "💡 " + lesson.Tip;
        }

        DifficultyInfo.Text = SelectedDifficulty() switch
        {
            Difficulty.Easy => "медленные враги · 3 щита · 2 ЭМИ на волну",
            Difficulty.Hard => "быстрые враги · без щитов · 1 ЭМИ на волну",
            _ => "обычная скорость · 2 щита · 1 ЭМИ на волну",
        };

        var checkpoint = lesson == null && ModeFree.IsChecked == true
            ? _scores.GetCheckpoint(ScoreKey(SelectedBank(), SelectedDifficulty()))
            : null;
        ContinueButton.Visibility = checkpoint != null ? Visibility.Visible : Visibility.Collapsed;
        if (checkpoint != null)
            ContinueButton.Content = $"⟳  ПРОДОЛЖИТЬ С ВОЛНЫ {checkpoint.Wave + 1}  ·  счёт {checkpoint.Score.ToString("N0", CultureInfo.CurrentCulture)}";

        if (!_loadingSettings)
        {
            if (LangRu.IsChecked == true)
                _settings.Language = "ru";
            else if (LangEn.IsChecked == true)
                _settings.Language = "en";
            _settings.LessonsMode = ModeLessons.IsChecked == true;
            _settings.Difficulty = SelectedDifficulty();
            _settings.Save();
        }

        string bankId = lesson != null ? $"{SelectedBank().Id}-lesson{lesson.Number}" : SelectedBank().Id;
        var best = _scores.Get($"{bankId}|{SelectedDifficulty()}");
        BestText.Text = best == null
            ? "Рекорда пока нет — самое время его поставить!"
            : $"Рекорд: {best.Score.ToString("N0", CultureInfo.CurrentCulture)} · волна {best.Wave} · {best.Wpm:0} WPM";
    }

    WordBank SelectedBank()
    {
        if (LangRu.IsChecked == true)
            return WordBank.Russian;
        if (LangCustom.IsChecked == true && _customBank != null)
            return _customBank;
        return WordBank.English;
    }

    Difficulty SelectedDifficulty() =>
        DiffEasy.IsChecked == true ? Difficulty.Easy :
        DiffHard.IsChecked == true ? Difficulty.Hard :
        Difficulty.Normal;

    /// <summary>Версия из свойства &lt;Version&gt; проекта.</summary>
    static string AppVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "?";

    static string ScoreKey(WordBank bank, Difficulty difficulty) => $"{bank.Id}|{difficulty}";

    /// <summary>Переключает раскладку на язык слов, если она установлена в системе.</summary>
    static void TrySwitchKeyboardLayout(string? cultureName)
    {
        if (cultureName == null)
            return;
        try
        {
            var target = CultureInfo.GetCultureInfo(cultureName);
            var manager = InputLanguageManager.Current;
            if (manager.CurrentInputLanguage?.TwoLetterISOLanguageName == target.TwoLetterISOLanguageName)
                return;
            foreach (var item in manager.AvailableInputLanguages ?? Array.Empty<object>())
            {
                if (item is CultureInfo culture && culture.TwoLetterISOLanguageName == target.TwoLetterISOLanguageName)
                {
                    manager.CurrentInputLanguage = culture;
                    return;
                }
            }
        }
        catch
        {
            // Не получилось — игрок увидит подсказку о раскладке в игре
        }
    }
}
