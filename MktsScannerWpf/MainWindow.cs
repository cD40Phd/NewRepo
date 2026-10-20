using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MktsScanner;

namespace MktsScannerWpf;

/// <summary>Строка таблицы найденных приборов.</summary>
public sealed class FoundRow
{
    public int Address { get; set; }
    public string Settings { get; set; } = "";
    public string Status { get; set; } = "";
    public string Details { get; set; } = "";
    public string Frame { get; set; } = "";
}

public sealed class MainWindow : Window
{
    private static readonly int[] BaudRates = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];

    private readonly ComboBox _portBox = new() { Width = 90, IsEditable = true };
    private readonly ComboBox _baudBox = new() { Width = 80, IsEditable = true };
    private readonly ComboBox _parityBox = new() { Width = 70 };
    private readonly ComboBox _stopBox = new() { Width = 45 };
    private readonly CheckBox _dtrRtsBox = new() { Content = "DTR/RTS", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _connectButton = new() { Content = "Подключить", Width = 100 };
    private readonly TextBlock _statusText = new() { Text = "Отключено", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };

    private readonly TextBox _timeoutBox = NumberBox("1000");
    private readonly TextBox _pauseBox = NumberBox("20");
    private readonly TextBox _retriesBox = NumberBox("1");
    private readonly TextBox _charTimeoutBox = NumberBox("0");
    private readonly CheckBox _echoBox = new() { Content = "Отбрасывать эхо", VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _verboseBox = new() { Content = "Показывать все кадры", VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBox _fromBox = NumberBox("1");
    private readonly TextBox _toBox = NumberBox("247");
    private readonly ComboBox _functionBox = new() { Width = 55 };
    private readonly TextBox _registerBox = NumberBox("0");
    private readonly TextBox _countBox = NumberBox("2");
    private readonly CheckBox _allBaudsBox = new() { Content = "Все скорости", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _scanButton = new() { Content = "Найти приборы", Width = 110 };
    private readonly Button _stopButton = new() { Content = "Стоп", Width = 60, IsEnabled = false };
    private readonly ProgressBar _progress = new() { Width = 180, Height = 16 };
    private readonly TextBlock _progressText = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly ObservableCollection<FoundRow> _found = new();
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false };
    private readonly TextBox _logBox = new()
    {
        IsReadOnly = true,
        FontFamily = new FontFamily("Consolas"),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        TextWrapping = TextWrapping.NoWrap
    };
    private readonly TextBox _commandBox = new() { FontFamily = new FontFamily("Consolas"), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox _crcBox = new() { Content = "Добавлять CRC", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _sendButton = new() { Content = "Отправить", Width = 90 };

    private readonly List<string> _history = new();
    private int _historyIndex;

    private BusScanner? _scanner;
    private SerialSettings? _settings;
    private CancellationTokenSource? _cancel;
    private bool _busy;

    public MainWindow()
    {
        Title = "Поиск приборов МКТС (Modbus RTU)";
        Width = 1000;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        foreach (int baud in BaudRates) _baudBox.Items.Add(baud.ToString());
        _baudBox.Text = "9600";
        foreach (Parity p in new[] { Parity.None, Parity.Even, Parity.Odd }) _parityBox.Items.Add(p);
        _parityBox.SelectedIndex = 0;
        _stopBox.Items.Add("1");
        _stopBox.Items.Add("2");
        _stopBox.SelectedIndex = 0;
        foreach (string f in new[] { "3", "4", "17" }) _functionBox.Items.Add(f);
        _functionBox.SelectedIndex = 0;

        Content = BuildLayout();
        RefreshPorts();
        UpdateControls();

        _connectButton.Click += async (_, _) => await ToggleConnectionAsync();
        _scanButton.Click += async (_, _) => await ScanAsync(null, null);
        _stopButton.Click += (_, _) => _cancel?.Cancel();
        _sendButton.Click += async (_, _) => await ExecuteCommandAsync();
        _commandBox.PreviewKeyDown += OnCommandKeyDown;
        Closed += (_, _) =>
        {
            _cancel?.Cancel();
            _scanner?.Dispose();
        };

        Log("Введите команду внизу окна (help — список команд) или нажмите «Найти приборы».");
    }

    private UIElement BuildLayout()
    {
        var refreshButton = new Button { Content = "↻", Width = 26, ToolTip = "Обновить список портов" };
        refreshButton.Click += (_, _) => RefreshPorts();

        var connection = Row(
            Label("Порт"), _portBox, refreshButton,
            Label("Скорость"), _baudBox,
            Label("Чётность"), _parityBox,
            Label("Стоп"), _stopBox,
            _dtrRtsBox, _connectButton, _statusText);

        var timing = Row(
            Label("Ожидание ответа, мс"), _timeoutBox,
            Label("Пауза, мс"), _pauseBox,
            Label("Повторы"), _retriesBox,
            Label("Конец кадра, мс (0 — авто)"), _charTimeoutBox,
            _echoBox, _verboseBox);

        var scan = Row(
            Label("Адреса с"), _fromBox, Label("по"), _toBox,
            Label("Функция"), _functionBox,
            Label("Регистр"), _registerBox,
            Label("Кол-во"), _countBox,
            _allBaudsBox, _scanButton, _stopButton, _progress, _progressText);

        var top = new StackPanel();
        top.Children.Add(Group("Подключение", connection));
        top.Children.Add(Group("Параметры обмена", timing));
        top.Children.Add(Group("Поиск", scan));

        _grid.ItemsSource = _found;
        _grid.Columns.Add(Column("Адрес", nameof(FoundRow.Address), 60));
        _grid.Columns.Add(Column("Параметры", nameof(FoundRow.Settings), 120));
        _grid.Columns.Add(Column("Статус", nameof(FoundRow.Status), 90));
        _grid.Columns.Add(Column("Ответ", nameof(FoundRow.Details), 380));
        _grid.Columns.Add(Column("Кадр", nameof(FoundRow.Frame), 250));

        var clearButton = new Button { Content = "Очистить", Width = 80 };
        clearButton.Click += (_, _) => { _logBox.Clear(); _found.Clear(); };
        var csvButton = new Button { Content = "Сохранить CSV", Width = 110 };
        csvButton.Click += (_, _) => SaveCsv();

        var commandLine = new DockPanel { Margin = new Thickness(6) };
        var commandButtons = Row(_crcBox, _sendButton, clearButton, csvButton);
        DockPanel.SetDock(commandButtons, Dock.Right);
        commandLine.Children.Add(commandButtons);
        commandLine.Children.Add(_commandBox);

        var center = new Grid { Margin = new Thickness(6, 0, 6, 0) };
        center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
        center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.3, GridUnitType.Star) });
        var splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 5 };
        Grid.SetRow(_grid, 0);
        Grid.SetRow(splitter, 1);
        Grid.SetRow(_logBox, 2);
        center.Children.Add(_grid);
        center.Children.Add(splitter);
        center.Children.Add(_logBox);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(commandLine, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(commandLine);
        root.Children.Add(center);
        return root;
    }

    // ---------- Подключение ----------

    private void RefreshPorts()
    {
        string current = _portBox.Text;
        var ports = SerialPort.GetPortNames()
            .Distinct()
            .OrderBy(p => int.TryParse(new string(p.Where(char.IsDigit).ToArray()), out int n) ? n : int.MaxValue)
            .ToList();
        _portBox.Items.Clear();
        foreach (string port in ports) _portBox.Items.Add(port);
        _portBox.Text = !string.IsNullOrEmpty(current) ? current : ports.FirstOrDefault() ?? "";
    }

    private async Task ToggleConnectionAsync()
    {
        if (_scanner != null)
        {
            _scanner.Dispose();
            _scanner = null;
            _settings = null;
            Log("Порт закрыт.");
            UpdateControls();
            return;
        }

        SerialSettings settings;
        try
        {
            settings = ReadSerialSettings();
        }
        catch (FormatException ex)
        {
            Log("[ОШИБКА] " + ex.Message);
            return;
        }

        bool dtrRts = _dtrRtsBox.IsChecked == true;
        SetBusy(true);
        try
        {
            Log($"Открытие {settings.PortName}, {settings}, DTR/RTS {(dtrRts ? "вкл" : "выкл")}...");
            // Открытие сетевого COM-порта (RealCOM) может занимать секунды — не блокируем окно.
            _scanner = await Task.Run(() => new BusScanner(settings, 1000, 20, false, dtrRts));
            _settings = settings;
            Log("Порт открыт.");
        }
        catch (Exception ex)
        {
            Log($"[ОШИБКА] Не удалось открыть {settings.PortName}: {ex.Message}");
            Log("  Проверьте: порт не занят другой программой (ПО МКТС, PowerShell-скрипт, терминал);");
            Log("  для RealCOM/NPort — что преобразователь доступен по сети; попробуйте переключить DTR/RTS.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private SerialSettings ReadSerialSettings()
    {
        string port = _portBox.Text.Trim();
        if (port.Length == 0) throw new FormatException("Не выбран COM-порт.");
        int baud = ParseNumber(_baudBox.Text, "Скорость", 300, 921600);
        var parity = (Parity)_parityBox.SelectedItem;
        var stop = _stopBox.SelectedIndex == 1 ? StopBits.Two : StopBits.One;
        return new SerialSettings(port, baud, parity, stop);
    }

    private void ApplyTiming()
    {
        if (_scanner == null) return;
        _scanner.AnswerTimeoutMs = ParseNumber(_timeoutBox.Text, "Ожидание ответа", 10, 30000);
        _scanner.PauseMs = ParseNumber(_pauseBox.Text, "Пауза", 0, 10000);
        _scanner.CharTimeoutMs = ParseNumber(_charTimeoutBox.Text, "Конец кадра", 0, 5000);
        _scanner.SkipEcho = _echoBox.IsChecked == true;

        // Скорость и чётность можно менять «на ходу», без переподключения.
        var settings = ReadSerialSettings();
        if (_settings != null && settings != _settings with { PortName = settings.PortName })
        {
            _scanner.ChangeSettings(settings.BaudRate, settings.Parity, settings.StopBits);
            _settings = _settings with { BaudRate = settings.BaudRate, Parity = settings.Parity, StopBits = settings.StopBits };
            Log($"Параметры порта изменены: {_settings}");
        }
    }

    // ---------- Поиск ----------

    private async Task ScanAsync(int? from, int? to)
    {
        if (_scanner == null || _settings == null) { Log("Сначала подключитесь к порту."); return; }

        int first, last, retries;
        byte function;
        ushort register, count;
        try
        {
            ApplyTiming();
            first = from ?? ParseNumber(_fromBox.Text, "Адрес с", 1, 247);
            last = to ?? ParseNumber(_toBox.Text, "Адрес по", 1, 247);
            if (first > last) throw new FormatException("Начальный адрес больше конечного.");
            retries = ParseNumber(_retriesBox.Text, "Повторы", 0, 10);
            function = (byte)ParseNumber((string)_functionBox.SelectedItem, "Функция", 1, 255);
            register = (ushort)ParseNumber(_registerBox.Text, "Регистр", 0, 65535);
            count = (ushort)ParseNumber(_countBox.Text, "Кол-во", 1, 125);
        }
        catch (FormatException ex)
        {
            Log("[ОШИБКА] " + ex.Message);
            return;
        }

        var bauds = _allBaudsBox.IsChecked == true ? BaudRates : new[] { _settings.BaudRate };
        bool verbose = _verboseBox.IsChecked == true;
        var scanner = _scanner;
        var baseSettings = _settings;
        int total = bauds.Length * (last - first + 1);
        int done = 0, found = 0;

        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        SetBusy(true);
        _progress.Maximum = total;
        _progress.Value = 0;

        try
        {
            await Task.Run(() =>
            {
                foreach (int baud in bauds)
                {
                    if (token.IsCancellationRequested) break;
                    var settings = baseSettings with { BaudRate = baud };
                    scanner.ChangeSettings(baud, settings.Parity, settings.StopBits);
                    Log($"Поиск: {settings.PortName}, {settings}, адреса {first}..{last}, функция {function}, регистр {register}, кол-во {count}");

                    for (int address = first; address <= last && !token.IsCancellationRequested; address++)
                    {
                        ProbeResult result = new(ProbeStatus.NoAnswer, [], "");
                        for (int attempt = 0; attempt <= retries; attempt++)
                        {
                            result = scanner.Probe((byte)address, function, register, count);
                            if (result.Status is ProbeStatus.Answer or ProbeStatus.Exception) break;
                        }

                        if (result.Status is ProbeStatus.Answer or ProbeStatus.Exception)
                        {
                            found++;
                            var row = new FoundRow
                            {
                                Address = address,
                                Settings = settings.ToString(),
                                Status = result.Status == ProbeStatus.Answer ? "ответ" : "исключение",
                                Details = result.Details,
                                Frame = HexSpaced(result.Frame)
                            };
                            Dispatcher.BeginInvoke(new Action(() => _found.Add(row)));
                            Log($"[НАЙДЕН] адрес {address}: {result.Details}   RX {row.Frame}");
                        }
                        else if (result.Status == ProbeStatus.Garbage || verbose)
                        {
                            string text = result.Status == ProbeStatus.Garbage
                                ? $"[ПОМЕХА] адрес {address}: {result.Details}, RX {HexSpaced(result.Frame)}"
                                : $"  адрес {address}: нет ответа";
                            Log(text);
                        }

                        int progress = ++done;
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            _progress.Value = progress;
                            _progressText.Text = $"адрес {address}, найдено {found}";
                        }));
                    }
                }

                // Возвращаем скорость, выбранную пользователем.
                scanner.ChangeSettings(baseSettings.BaudRate, baseSettings.Parity, baseSettings.StopBits);
            });

            Log(token.IsCancellationRequested
                ? $"Поиск остановлен. Найдено приборов: {found}."
                : $"Поиск завершён. Найдено приборов: {found}.");
            if (found == 0)
                Log("  Нет ответов: проверьте скорость/чётность, линию A/B, увеличьте «Ожидание ответа» (для RealCOM 1000–2000 мс).");
        }
        catch (Exception ex)
        {
            Log("[ОШИБКА] " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------- Командная строка ----------

    private void OnCommandKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _ = ExecuteCommandAsync();
                break;
            case Key.Up when _history.Count > 0:
                _historyIndex = Math.Max(0, _historyIndex - 1);
                _commandBox.Text = _history[_historyIndex];
                _commandBox.CaretIndex = _commandBox.Text.Length;
                e.Handled = true;
                break;
            case Key.Down when _history.Count > 0:
                _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
                _commandBox.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
                _commandBox.CaretIndex = _commandBox.Text.Length;
                e.Handled = true;
                break;
        }
    }

    private async Task ExecuteCommandAsync()
    {
        if (_busy) return;
        string line = _commandBox.Text.Trim();
        if (line.Length == 0) return;

        _history.Remove(line);
        _history.Add(line);
        _historyIndex = _history.Count;
        _commandBox.Clear();
        Log("> " + line);

        string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        string verb = parts[0].ToLowerInvariant();
        try
        {
            switch (verb)
            {
                case "help":
                case "?":
                    Log(HelpText);
                    return;
                case "clear":
                case "cls":
                    _logBox.Clear();
                    return;
                case "ports":
                    RefreshPorts();
                    Log("Порты: " + string.Join(", ", _portBox.Items.Cast<string>()));
                    return;
                case "scan":
                    await ScanAsync(
                        parts.Length > 1 ? ParseNumber(parts[1], "Адрес с", 1, 247) : null,
                        parts.Length > 2 ? ParseNumber(parts[2], "Адрес по", 1, 247) : null);
                    return;
                case "read":
                case "readin":
                    if (parts.Length < 2) throw new FormatException($"Формат: {verb} <адрес> [регистр] [кол-во]");
                    byte function = verb == "read" ? ModbusRtu.ReadHoldingRegisters : ModbusRtu.ReadInputRegisters;
                    byte address = (byte)ParseNumber(parts[1], "Адрес", 0, 247);
                    ushort register = (ushort)(parts.Length > 2 ? ParseNumber(parts[2], "Регистр", 0, 65535) : 0);
                    ushort count = (ushort)(parts.Length > 3 ? ParseNumber(parts[3], "Кол-во", 1, 125) : 2);
                    await SendFrameAsync(ModbusRtu.BuildRequest(address, function, register, count));
                    return;
                case "id":
                    if (parts.Length < 2) throw new FormatException("Формат: id <адрес>");
                    await SendFrameAsync(ModbusRtu.BuildRequest((byte)ParseNumber(parts[1], "Адрес", 0, 247), ModbusRtu.ReportSlaveId, 0, 0));
                    return;
                default:
                    byte[] frame = ParseHex(line);
                    if (_crcBox.IsChecked == true)
                    {
                        ushort crc = ModbusRtu.Crc16(frame, frame.Length);
                        frame = [.. frame, (byte)(crc & 0xFF), (byte)(crc >> 8)];
                    }
                    await SendFrameAsync(frame);
                    return;
            }
        }
        catch (FormatException ex)
        {
            Log("[ОШИБКА] " + ex.Message);
        }
    }

    private async Task SendFrameAsync(byte[] request)
    {
        if (_scanner == null) { Log("Сначала подключитесь к порту."); return; }
        ApplyTiming();

        // Для чтения регистров длина ответа известна из заголовка; иначе ждём паузы в линии.
        byte? function = request.Length >= 2 && request[1] is ModbusRtu.ReadHoldingRegisters or ModbusRtu.ReadInputRegisters or ModbusRtu.ReportSlaveId
            ? request[1]
            : null;

        var scanner = _scanner;
        SetBusy(true);
        try
        {
            Log("TX " + HexSpaced(request));
            var started = DateTime.Now;
            byte[] response = await Task.Run(() => scanner.Transact(request, function));
            int elapsed = (int)(DateTime.Now - started).TotalMilliseconds;

            if (response.Length == 0)
            {
                Log($"RX — нет ответа ({elapsed} мс)");
                return;
            }

            Log($"RX {HexSpaced(response)}  ({elapsed} мс, CRC {(ModbusRtu.CheckCrc(response) ? "OK" : "ошибка")})");
            if (function is byte fn)
            {
                var result = ModbusRtu.Parse(response, request[0], fn);
                Log($"   {result.Status}: {result.Details}");
            }
        }
        catch (Exception ex)
        {
            Log("[ОШИБКА] " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private const string HelpText = """
        Команды:
          scan [с] [по]                поиск приборов (без параметров — диапазон из полей «Адреса с/по»)
          read <адрес> [рег] [кол]     чтение holding-регистров (функция 3), по умолчанию рег 0, кол 2
          readin <адрес> [рег] [кол]   чтение input-регистров (функция 4)
          id <адрес>                   Report Slave ID (функция 17)
          02 03 00 00 00 02            произвольный кадр в HEX; CRC добавится, если отмечено «Добавлять CRC»
          ports                        обновить список COM-портов
          clear                        очистить журнал
        Числа можно писать в десятичном виде или как 0x1F. Стрелки ↑/↓ — история команд.
        """;

    // ---------- Вспомогательное ----------

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateControls();
    }

    private void UpdateControls()
    {
        bool connected = _scanner != null;
        _connectButton.Content = connected ? "Отключить" : "Подключить";
        _connectButton.IsEnabled = !_busy;
        _portBox.IsEnabled = !connected && !_busy;
        _dtrRtsBox.IsEnabled = !connected && !_busy;
        _scanButton.IsEnabled = connected && !_busy;
        _sendButton.IsEnabled = !_busy;
        _stopButton.IsEnabled = _busy && _cancel != null && !_cancel.IsCancellationRequested;
        _statusText.Text = connected && _settings != null ? $"Подключено: {_settings.PortName}" : "Отключено";
        _statusText.Foreground = connected ? Brushes.Green : Brushes.Gray;
        if (!_busy) _cancel = null;
    }

    private void Log(string text)
    {
        // Всегда через очередь диспетчера: иначе строки из фонового поиска,
        // ещё стоящие в очереди, окажутся в журнале после итоговой строки.
        string stamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
                _logBox.AppendText($"[{stamp}] {line}{Environment.NewLine}");
            _logBox.ScrollToEnd();
        }));
    }

    private void SaveCsv()
    {
        if (_found.Count == 0) { Log("Таблица пуста — сохранять нечего."); return; }
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "mkts.csv" };
        if (dialog.ShowDialog(this) != true) return;

        var csv = new StringBuilder("port;settings;address;status;details;frame_hex\r\n");
        string port = _settings?.PortName ?? _portBox.Text;
        foreach (var row in _found)
            csv.Append($"{port};{row.Settings};{row.Address};{row.Status};\"{row.Details.Replace("\"", "\"\"")}\";{row.Frame}\r\n");
        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
        Log("Сохранено: " + dialog.FileName);
    }

    private static byte[] ParseHex(string text)
    {
        var bytes = new List<byte>();
        foreach (string raw in text.Split(new[] { ' ', ',', ';', '\t', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw.Substring(2) : raw;
            if (token.Length % 2 != 0) token = "0" + token;
            for (int i = 0; i < token.Length; i += 2)
            {
                if (!byte.TryParse(token.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    throw new FormatException($"Не понял '{raw}': ожидались HEX-байты или команда (help — список команд).");
                bytes.Add(b);
            }
        }
        if (bytes.Count == 0) throw new FormatException("Пустой кадр.");
        return bytes.ToArray();
    }

    private static int ParseNumber(string text, string name, int min, int max)
    {
        text = text.Trim();
        bool ok = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int n)
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
        if (!ok || n < min || n > max)
            throw new FormatException($"{name}: ожидалось число от {min} до {max}, получено '{text}'.");
        return n;
    }

    private static string HexSpaced(byte[] data) => string.Join(" ", data.Select(b => b.ToString("X2")));

    private static TextBox NumberBox(string value) => new() { Text = value, Width = 50, VerticalContentAlignment = VerticalAlignment.Center };

    private static TextBlock Label(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };

    private static StackPanel Row(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            if (child is FrameworkElement element && element is not TextBlock)
                element.Margin = new Thickness(2, 2, 6, 2);
            panel.Children.Add(child);
        }
        return panel;
    }

    private static GroupBox Group(string header, UIElement content) =>
        new() { Header = header, Content = content, Margin = new Thickness(6, 4, 6, 0), Padding = new Thickness(2) };

    private static DataGridTextColumn Column(string header, string path, double width) =>
        new() { Header = header, Binding = new Binding(path), Width = new DataGridLength(width) };
}
