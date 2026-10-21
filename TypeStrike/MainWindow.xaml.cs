using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TypeStrike.Camera;
using TypeStrike.Game;

namespace TypeStrike;

public partial class MainWindow : Window
{
    enum Screen { Menu, Game, Paused, GameOver }

    readonly GameEngine _engine = new();
    readonly SoundEngine _sound = new();
    readonly ScoreStore _scores = ScoreStore.Load();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _previousTime;
    Screen _screen = Screen.Menu;
    WordBank? _customBank;
    RadioButton _lastLanguage;
    DateTime _gameOverShownAt;
    LessonPlan? _currentPlan;
    GazeTracker? _gaze;

    public MainWindow()
    {
        InitializeComponent();
        _lastLanguage = LangEn;

        View.Engine = _engine;
        Hints.Engine = _engine;
        _engine.PlaySound = _sound.Play;
        _engine.GameOver += OnGameOver;

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

        if (_screen != Screen.Paused)
            _engine.Update(dt);
        View.InvalidateVisual();
        if (Hints.Visibility == Visibility.Visible)
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
                        _gaze?.Recalibrate();
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

    void StartGame()
    {
        var bank = SelectedBank();
        var difficulty = SelectedDifficulty();
        _sound.Enabled = SoundBox.IsChecked == true;
        TrySwitchKeyboardLayout(bank.KeyboardCulture);

        var lesson = SelectedLesson();
        _currentPlan = lesson != null ? LessonCatalog.CreatePlan(bank, lesson) : null;
        _engine.Start(bank, difficulty, _currentPlan);

        Hints.Visibility = HintsBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (CameraBox.IsChecked == true)
            StartCamera();
        else
            StopCamera();
        MenuPanel.Visibility = Visibility.Collapsed;
        PausePanel.Visibility = Visibility.Collapsed;
        GameOverPanel.Visibility = Visibility.Collapsed;
        _screen = Screen.Game;
        Keyboard.Focus(View);
    }

    void Pause()
    {
        _screen = Screen.Paused;
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
        _engine.Reset();
        StopCamera();
        Hints.Visibility = Visibility.Collapsed;
        PausePanel.Visibility = Visibility.Collapsed;
        GameOverPanel.Visibility = Visibility.Collapsed;
        MenuPanel.Visibility = Visibility.Visible;
        _screen = Screen.Menu;
        UpdateMenuInfo();
    }

    void OnGameOver(GameResult result)
    {
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
        if (_gaze != null)
            AddStat("Взгляд на клавиатуру", result.Peeks == 0 ? "ни разу 👍" : $"{result.Peeks} раз",
                result.Peeks == 0 ? "#6BFFB0" : "#FF9F1C");

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

    void StartCamera()
    {
        if (_gaze == null)
        {
            var tracker = new GazeTracker(Dispatcher);
            // События уже остановленного трекера могут прийти позже — игнорируем их
            tracker.StateChanged += state =>
            {
                if (_gaze == tracker)
                    OnGazeStateChanged(state);
            };
            tracker.Preview += bitmap =>
            {
                if (_gaze == tracker)
                    CameraImage.Source = bitmap;
            };
            _gaze = tracker;
            tracker.Start();
            OnGazeStateChanged(GazeState.Starting);
        }
        else
        {
            // Калибруемся заново в начале каждой игры, пока висит баннер «Приготовьтесь»
            _gaze.Recalibrate();
        }
        CameraPanel.Visibility = Visibility.Visible;
    }

    void StopCamera()
    {
        _gaze?.Dispose();
        _gaze = null;
        _engine.SetLookingDown(false);
        CameraImage.Source = null;
        CameraPanel.Visibility = Visibility.Collapsed;
    }

    void OnGazeStateChanged(GazeState state)
    {
        CameraStatus.Text = state switch
        {
            GazeState.Starting => "Включаю камеру…",
            GazeState.Calibrating => "Калибровка: смотрите на экран",
            GazeState.OnScreen => "✔ Взгляд на экране",
            GazeState.LookingDown => "👀 Взгляд на клавиатуре!",
            GazeState.NoFace => "Лицо не в кадре",
            _ => "Камера недоступна: " + _gaze?.ErrorMessage,
        };
        CameraStatus.Foreground = (Brush)new BrushConverter().ConvertFromString(state switch
        {
            GazeState.OnScreen => "#6BFFB0",
            GazeState.LookingDown or GazeState.Error => "#FF9F1C",
            _ => "#C8D3EA",
        })!;
        _engine.SetLookingDown(state == GazeState.LookingDown);
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
