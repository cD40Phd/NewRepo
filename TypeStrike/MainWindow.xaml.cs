using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
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
        ShowLessonOutcome(result);

        StatsGrid.Children.Clear();
        StatsGrid.RowDefinitions.Clear();
        AddStat("Счёт", result.Score.ToString("N0", CultureInfo.CurrentCulture), "#FF9F1C");
        AddStat("Волна", result.Wave.ToString(CultureInfo.InvariantCulture));
        AddStat("Скорость", $"{result.Wpm:0} WPM  ({result.Wpm * 5:0} зн/мин)");
        AddStat("Точность", $"{result.Accuracy:0.#}%");
        AddStat("Макс. комбо", result.MaxCombo.ToString(CultureInfo.InvariantCulture));
        AddStat("Сбито кораблей", result.Words.ToString(CultureInfo.InvariantCulture));

        TroubleText.Text = (string.IsNullOrEmpty(result.TroubleLetters)
            ? "Ни одной ошибки — отличная работа!"
            : "Чаще всего ошибались на буквах:\n" + result.TroubleLetters)
            + (result.Lesson != null ? "\n\nСовет: " + LessonCatalog.Tips[Random.Shared.Next(LessonCatalog.Tips.Length)] : "");

        GameOverPanel.Visibility = Visibility.Visible;
        _screen = Screen.GameOver;
        _gameOverShownAt = DateTime.Now;
    }

    void ShowLessonOutcome(GameResult result)
    {
        bool lesson = result.Lesson != null && _currentPlan != null;
        bool passed = lesson && result.Completed && result.Stars > 0;

        if (!lesson)
        {
            GameOverTitle.Text = "КОРАБЛЬ УНИЧТОЖЕН";
            GameOverTitle.Foreground = (Brush)new BrushConverter().ConvertFromString("#FF4D6D")!;
        }
        else if (passed)
        {
            GameOverTitle.Text = "УРОК ПРОЙДЕН";
            GameOverTitle.Foreground = (Brush)new BrushConverter().ConvertFromString("#6BFFB0")!;
        }
        else
        {
            GameOverTitle.Text = result.Completed ? "НУЖНО ТОЧНЕЕ" : "КОРАБЛЬ УНИЧТОЖЕН";
            GameOverTitle.Foreground = (Brush)new BrushConverter().ConvertFromString(result.Completed ? "#FF9F1C" : "#FF4D6D")!;
        }

        ResultStars.Visibility = lesson ? Visibility.Visible : Visibility.Collapsed;
        ResultStars.Text = new string('★', result.Stars) + new string('☆', 3 - result.Stars);

        var lessons = LessonCatalog.For(SelectedBank());
        bool hasNext = passed && result.Lesson!.Number < lessons.Count;
        NextLessonButton.Visibility = hasNext ? Visibility.Visible : Visibility.Collapsed;
        RestartButton.Content = lesson ? "ПОВТОРИТЬ УРОК" + (hasNext ? "" : "  [Enter]") : "ИГРАТЬ СНОВА  [Enter]";

        if (passed)
        {
            _scores.SubmitStars(_currentPlan!.Key, result.Stars);
            if (hasNext)
                _scores.SetCurrentLesson(SelectedBank().Id, result.Lesson!.Number);
        }
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
        int index = Math.Clamp(_scores.GetCurrentLesson(bank.Id) + delta, 0, lessons.Count - 1);
        _scores.SetCurrentLesson(bank.Id, index);
        UpdateMenuInfo();
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
        return lessons[Math.Clamp(_scores.GetCurrentLesson(bank.Id), 0, lessons.Count - 1)];
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
            int stars = _scores.StarsFor($"{bank.Id}-lesson{lesson.Number}");
            int passed = lessons.Count(l => _scores.StarsFor($"{bank.Id}-lesson{l.Number}") > 0);
            LessonTitle.Text = $"{lesson.Title}   ({lesson.Number}/{lessons.Count})";
            LessonLetters.Text = $"изученные буквы: {lesson.Letters.ToUpperInvariant()}  ·  пройдено уроков: {passed}";
            LessonStars.Text = new string('★', stars) + new string('☆', 3 - stars);
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
