using System.IO.Ports;

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
    private readonly int _answerTimeoutMs;
    private readonly int _charTimeoutMs;
    private readonly int _pauseMs;
    private readonly bool _skipEcho;

    public BusScanner(SerialSettings settings, int answerTimeoutMs, int pauseMs, bool skipEcho)
    {
        _answerTimeoutMs = answerTimeoutMs;
        _pauseMs = pauseMs;
        _skipEcho = skipEcho;

        // Межсимвольный тайм-аут: 3.5 символа по Modbus, но не меньше 20 мс —
        // USB-преобразователи и таймер Windows отдают байты пачками.
        double charTimeMs = 11 * 1000.0 / settings.BaudRate;
        _charTimeoutMs = Math.Max(20, (int)Math.Ceiling(charTimeMs * 3.5));

        _port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, 8, settings.StopBits)
        {
            Handshake = Handshake.None,
            ReadTimeout = answerTimeoutMs,
            WriteTimeout = 1000
        };
        _port.Open();
    }

    public ProbeResult Probe(byte address, byte function, ushort register, ushort count)
    {
        byte[] request = ModbusRtu.BuildRequest(address, function, register, count);

        Thread.Sleep(_pauseMs);
        _port.DiscardInBuffer();
        _port.Write(request, 0, request.Length);

        if (_skipEcho)
        {
            // Некоторые преобразователи RS-485 возвращают в линию собственную передачу.
            var echo = ReadBytes(request.Length, _answerTimeoutMs);
            if (echo.Count < request.Length)
                return ModbusRtu.Parse(echo.ToArray(), address, function);
        }

        var frame = ReadBytes(3, _answerTimeoutMs);
        if (frame.Count == 3)
        {
            int? expected = ModbusRtu.ExpectedLength(frame.ToArray(), function);
            if (expected is int length)
                frame.AddRange(ReadBytes(length - 3, _charTimeoutMs));
        }

        // Добираем хвост, чтобы мусор не попал в ответ на следующий адрес.
        frame.AddRange(ReadBytes(int.MaxValue, _charTimeoutMs, readAll: true));
        return ModbusRtu.Parse(frame.ToArray(), address, function);
    }

    private List<byte> ReadBytes(int count, int firstByteTimeoutMs, bool readAll = false)
    {
        var result = new List<byte>();
        _port.ReadTimeout = firstByteTimeoutMs;
        try
        {
            while (result.Count < count)
            {
                if (readAll && _port.BytesToRead == 0)
                {
                    Thread.Sleep(_charTimeoutMs);
                    if (_port.BytesToRead == 0) break;
                }

                result.Add((byte)_port.ReadByte());
                _port.ReadTimeout = _charTimeoutMs;
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
