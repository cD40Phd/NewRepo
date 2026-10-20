using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.IO.Ports;
using System.Text;
using MktsScanner;

#if NET
Console.OutputEncoding = Encoding.UTF8;
#endif

Options options;
try
{
    options = Options.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"[ОШИБКА] {ex.Message}");
    Console.Error.WriteLine("Справка: MktsScanner --help");
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(Options.HelpText);
    return 0;
}

if (options.ListPorts || options.PortName == null)
{
    string[] ports = SerialPort.GetPortNames();
    Console.WriteLine(ports.Length == 0 ? "Последовательные порты не найдены." : "Доступные порты: " + string.Join(", ", ports));
    if (options.PortName == null && !options.ListPorts)
        Console.WriteLine("Укажите порт: MktsScanner --port COM3");
    return 0;
}

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
    Console.WriteLine();
    Console.WriteLine("[INFO] Остановка по Ctrl+C, вывожу найденное...");
};

var found = new List<FoundDevice>();
int garbageCount = 0;

foreach (int baud in options.BaudRates)
foreach (Parity parity in options.Parities)
{
    if (cancel.IsCancellationRequested) break;

    var settings = new SerialSettings(options.PortName, baud, parity, options.StopBits);
    Console.WriteLine($"[INFO] {settings.PortName}, {settings}: адреса {options.FirstAddress}..{options.LastAddress}");

    BusScanner scanner;
    try
    {
        scanner = new BusScanner(settings, options.TimeoutMs, options.PauseMs, options.SkipEcho, options.DtrRts);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine($"[ОШИБКА] Не удалось открыть {settings.PortName}: {ex.Message}");
        return 1;
    }

    using (scanner)
    {
        for (int address = options.FirstAddress; address <= options.LastAddress && !cancel.IsCancellationRequested; address++)
        {
            Console.Write($"\r  адрес {address,3}...");

            ProbeResult result = new(ProbeStatus.NoAnswer, [], "");
            for (int attempt = 0; attempt <= options.Retries; attempt++)
            {
                result = scanner.Probe((byte)address, options.Function, options.Register, options.Count);
                if (result.Status is ProbeStatus.Answer or ProbeStatus.Exception) break;
            }

            switch (result.Status)
            {
                case ProbeStatus.Answer:
                case ProbeStatus.Exception:
                    found.Add(new FoundDevice(settings, (byte)address, result));
                    Console.WriteLine($"\r  [НАЙДЕН] адрес {address,3}: {result.Details}");
                    break;
                case ProbeStatus.Garbage:
                    garbageCount++;
                    if (options.Verbose)
                        Console.WriteLine($"\r  [ПОМЕХА] адрес {address,3}: {result.Details}, кадр {ModbusRtu.Hex(result.Frame)}");
                    break;
            }
        }
        Console.Write("\r                    \r");
    }
}

Console.WriteLine();
if (found.Count == 0)
{
    Console.WriteLine("Приборы не найдены.");
    if (garbageCount > 0)
        Console.WriteLine($"В линии были битые кадры ({garbageCount}) — проверьте скорость, чётность и полярность A/B (--verbose покажет кадры).");
    else
        Console.WriteLine("Проверьте подключение линии A/B, питание платы RS485E и скорость обмена в меню прибора (--baud all).");
}
else
{
    Console.WriteLine($"Найдено приборов: {found.Count}");
    Console.WriteLine($"{"Порт",-8} {"Параметры",-16} {"Адрес",5}  Ответ");
    foreach (var (settings, address, result) in found)
        Console.WriteLine($"{settings.PortName,-8} {settings,-16} {address,5}  {result.Details}");
}

if (options.CsvPath != null)
{
    var csv = new StringBuilder("port;baud;parity;stop_bits;address;status;details;frame_hex\n");
    foreach (var (settings, address, result) in found)
    {
        csv.AppendLine(string.Join(";",
            settings.PortName, settings.BaudRate, settings.Parity, settings.StopBits, address,
            result.Status, "\"" + result.Details.Replace("\"", "\"\"") + "\"", ModbusRtu.Hex(result.Frame)));
    }
    File.WriteAllText(options.CsvPath, csv.ToString(), new UTF8Encoding(true));
    Console.WriteLine($"[INFO] Результаты сохранены в {options.CsvPath}");
}

return found.Count > 0 ? 0 : 3;

internal sealed record FoundDevice(SerialSettings Settings, byte Address, ProbeResult Result);

internal sealed class Options
{
    public static readonly int[] AllBaudRates = [1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200];

    public string? PortName { get; private set; }
    public List<int> BaudRates { get; private set; } = [9600];
    public List<Parity> Parities { get; private set; } = [Parity.None];
    public StopBits StopBits { get; private set; } = StopBits.One;
    public int FirstAddress { get; private set; } = 1;
    public int LastAddress { get; private set; } = 247;
    public byte Function { get; private set; } = ModbusRtu.ReadHoldingRegisters;
    public ushort Register { get; private set; }
    public ushort Count { get; private set; } = 2;
    public int TimeoutMs { get; private set; } = 500;
    public int PauseMs { get; private set; } = 20;
    public int Retries { get; private set; } = 1;
    public bool SkipEcho { get; private set; }
    public bool DtrRts { get; private set; } = true;
    public bool Verbose { get; private set; }
    public string? CsvPath { get; private set; }
    public bool ListPorts { get; private set; }
    public bool ShowHelp { get; private set; }

    public const string HelpText = """
        Поиск приборов МКТС (плата RS485E, Modbus RTU) на линии RS-485/RS-232.

        Перебирает сетевые адреса и шлёт каждому короткий запрос Modbus. Прибор считается
        найденным, если вернул корректный ответ или ответ-исключение (значит, адрес занят).

        Использование:
          MktsScanner --port COM3 [параметры]
          MktsScanner --list

        Параметры:
          --port <имя>        COM-порт (COM3, /dev/ttyUSB0)
          --list              показать доступные порты
          --baud <список>     скорости через запятую или all (по умолчанию 9600)
          --parity <список>   none, even, odd через запятую или all (по умолчанию none)
          --stop <1|2>        стоп-биты (по умолчанию 1)
          --from <N>          первый адрес (по умолчанию 1)
          --to <N>            последний адрес (по умолчанию 247)
          --function <N>      функция Modbus: 3, 4 или 17 (0x11, Report Slave ID); по умолчанию 3
          --register <N>      начальный регистр для функций 3/4 (по умолчанию 0)
          --count <N>         число регистров для функций 3/4 (по умолчанию 2)
          --timeout <мс>      ожидание ответа (по умолчанию 500; для RealCOM 1000)
          --pause <мс>        пауза перед запросом (по умолчанию 20)
          --retries <N>       повторы для молчащего адреса (по умолчанию 1)
          --echo              отбрасывать эхо собственной передачи (некоторые преобразователи)
          --no-dtr-rts        не поднимать DTR/RTS (по умолчанию подняты, как нужно RealCOM)
          --verbose           показывать битые кадры
          --csv <файл>        сохранить найденные приборы в CSV

        Примеры:
          MktsScanner --port COM3
          MktsScanner --port COM3 --baud all --parity all --csv mkts.csv
          MktsScanner --port /dev/ttyUSB0 --from 1 --to 32 --function 17
        """;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i].ToLowerInvariant();
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Для {key} не указано значение.");

            switch (key)
            {
                case "-h": case "--help": case "/?": o.ShowHelp = true; break;
                case "--list": o.ListPorts = true; break;
                case "--port": o.PortName = Next(); break;
                case "--baud": o.BaudRates = ParseBaudRates(Next()); break;
                case "--parity": o.Parities = ParseParities(Next()); break;
                case "--stop":
                    o.StopBits = Next() switch
                    {
                        "1" => StopBits.One,
                        "2" => StopBits.Two,
                        var v => throw new ArgumentException($"Стоп-биты: 1 или 2, получено '{v}'.")
                    };
                    break;
                case "--from": o.FirstAddress = ParseInt(key, Next(), 1, 247); break;
                case "--to": o.LastAddress = ParseInt(key, Next(), 1, 247); break;
                case "--function":
                    o.Function = (byte)ParseInt(key, Next(), 0, 255);
                    if (o.Function is not (ModbusRtu.ReadHoldingRegisters or ModbusRtu.ReadInputRegisters or ModbusRtu.ReportSlaveId))
                        throw new ArgumentException("Поддерживаются функции 3, 4 и 17.");
                    break;
                case "--register": o.Register = (ushort)ParseInt(key, Next(), 0, 65535); break;
                case "--count": o.Count = (ushort)ParseInt(key, Next(), 1, 125); break;
                case "--timeout": o.TimeoutMs = ParseInt(key, Next(), 10, 10000); break;
                case "--pause": o.PauseMs = ParseInt(key, Next(), 0, 10000); break;
                case "--retries": o.Retries = ParseInt(key, Next(), 0, 10); break;
                case "--echo": o.SkipEcho = true; break;
                case "--no-dtr-rts": o.DtrRts = false; break;
                case "--verbose": case "-v": o.Verbose = true; break;
                case "--csv": o.CsvPath = Next(); break;
                default: throw new ArgumentException($"Неизвестный параметр '{args[i]}'.");
            }
        }

        if (o.FirstAddress > o.LastAddress)
            throw new ArgumentException("--from не может быть больше --to.");
        return o;
    }

    private static int ParseInt(string key, string value, int min, int max)
    {
        bool ok = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int n)
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
        if (!ok || n < min || n > max)
            throw new ArgumentException($"{key}: ожидалось число от {min} до {max}, получено '{value}'.");
        return n;
    }

    private static IEnumerable<string> SplitList(string value) =>
        value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => v.Length > 0);

    private static List<int> ParseBaudRates(string value)
    {
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase)) return [.. AllBaudRates];
        return SplitList(value)
            .Select(v => ParseInt("--baud", v, 300, 921600))
            .ToList();
    }

    private static List<Parity> ParseParities(string value)
    {
        if (value.Equals("all", StringComparison.OrdinalIgnoreCase)) return [Parity.None, Parity.Even, Parity.Odd];
        return SplitList(value)
            .Select(v => v.ToLowerInvariant() switch
            {
                "none" or "n" => Parity.None,
                "even" or "e" => Parity.Even,
                "odd" or "o" => Parity.Odd,
                _ => throw new ArgumentException($"--parity: none, even или odd, получено '{v}'.")
            })
            .ToList();
    }
}
