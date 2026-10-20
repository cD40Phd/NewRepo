using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace MktsScanner;

public sealed record SerialSettings(string PortName, int BaudRate, Parity Parity, StopBits StopBits)
{
    public override string ToString() =>
        $"{BaudRate} бод, 8{Parity.ToString()[0]}{(StopBits == StopBits.Two ? 2 : 1)}";
}

/// <summary>Опрос адресов на линии RS-485/RS-232 по Modbus RTU.</summary>
public sealed class BusScanner : IDisposable
{
    private readonly SerialPort _port;

    /// <summary>Ожидание первого байта ответа. Для RealCOM/NPort нужно 500–1000 мс.</summary>
    public int AnswerTimeoutMs { get; set; }

    /// <summary>Пауза в линии перед запросом.</summary>
    public int PauseMs { get; set; }

    /// <summary>Отбрасывать эхо собственной передачи (некоторые преобразователи RS-485).</summary>
    public bool SkipEcho { get; set; }

    /// <summary>
    /// Межсимвольный тайм-аут: после него кадр считается законченным. 0 — автоматически:
    /// 3.5 символа по Modbus, но не меньше 50 мс (USB- и сетевые преобразователи отдают байты пачками).
    /// </summary>
    public int CharTimeoutMs { get; set; }

    public BusScanner(SerialSettings settings, int answerTimeoutMs, int pauseMs, bool skipEcho, bool dtrRts = true)
    {
        AnswerTimeoutMs = answerTimeoutMs;
        PauseMs = pauseMs;
        SkipEcho = skipEcho;

        _port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, 8, settings.StopBits)
        {
            Handshake = Handshake.None,
            // Драйверы RealCOM (Moxa NPort и аналоги) без поднятых DTR/RTS могут не открывать порт или не передавать.
            DtrEnable = dtrRts,
            RtsEnable = dtrRts,
            ReadTimeout = answerTimeoutMs,
            WriteTimeout = 1000
        };
        _port.Open();
    }

    /// <summary>Сменить скорость/чётность без переоткрытия порта.</summary>
    public void ChangeSettings(int baudRate, Parity parity, StopBits stopBits)
    {
        _port.BaudRate = baudRate;
        _port.Parity = parity;
        _port.StopBits = stopBits;
    }

    public ProbeResult Probe(byte address, byte function, ushort register, ushort count)
    {
        byte[] request = ModbusRtu.BuildRequest(address, function, register, count);
        return ModbusRtu.Parse(Transact(request, function), address, function);
    }

    /// <summary>
    /// Отправить готовый кадр и принять ответ. Если функция известна, длина ответа берётся
    /// из заголовка Modbus; иначе приём идёт до паузы в линии.
    /// </summary>
    public byte[] Transact(byte[] request, byte? function)
    {
        Thread.Sleep(PauseMs);
        _port.DiscardInBuffer();
        _port.Write(request, 0, request.Length);

        int charTimeoutMs = CharTimeoutMs > 0
            ? CharTimeoutMs
            : Math.Max(50, (int)Math.Ceiling(11 * 1000.0 / _port.BaudRate * 3.5));

        if (SkipEcho)
        {
            var echo = ReadBytes(request.Length, AnswerTimeoutMs, charTimeoutMs);
            if (echo.Count < request.Length)
                return echo.ToArray();
        }

        List<byte> frame;
        if (function is byte fn)
        {
            frame = ReadBytes(3, AnswerTimeoutMs, charTimeoutMs);
            if (frame.Count == 3 && ModbusRtu.ExpectedLength(frame, fn) is int length)
                frame.AddRange(ReadBytes(length - 3, charTimeoutMs, charTimeoutMs));

            // Добираем хвост, чтобы мусор не попал в ответ на следующий адрес.
            frame.AddRange(ReadBytes(int.MaxValue, 0, charTimeoutMs, readAll: true));
        }
        else
        {
            frame = ReadBytes(1, AnswerTimeoutMs, charTimeoutMs);
            if (frame.Count > 0)
                frame.AddRange(ReadBytes(int.MaxValue, 0, charTimeoutMs, readAll: true));
        }
        return frame.ToArray();
    }

    private List<byte> ReadBytes(int count, int firstByteTimeoutMs, int charTimeoutMs, bool readAll = false)
    {
        var result = new List<byte>();
        _port.ReadTimeout = Math.Max(1, firstByteTimeoutMs);
        try
        {
            while (result.Count < count)
            {
                if (readAll && _port.BytesToRead == 0)
                {
                    Thread.Sleep(charTimeoutMs);
                    if (_port.BytesToRead == 0) break;
                }

                result.Add((byte)_port.ReadByte());
                _port.ReadTimeout = charTimeoutMs;
            }
        }
        catch (TimeoutException)
        {
            // Линия замолчала — возвращаем то, что успели принять.
        }
        return result;
    }

    public void Dispose() => _port.Dispose();
}
