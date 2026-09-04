using System.IO.Ports;

namespace EnergyBalance.Acquisition.Protocols;

/// <summary>
/// Реализация <see cref="ISerialTransport"/> поверх физического COM-порта
/// (для приборов, подключённых через USB-RS485 переходник или встроенный COM Meркурий 230/234 линии).
/// </summary>
public sealed class SerialPortTransport : ISerialTransport
{
    private readonly SerialPort _port;

    public SerialPortTransport(string portName, int baudRate, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
    {
        _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
        {
            ReadTimeout = 2000,
            WriteTimeout = 2000,
        };
    }

    public Task OpenAsync(CancellationToken ct = default)
    {
        if (!_port.IsOpen)
        {
            _port.Open();
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }

        return Task.CompletedTask;
    }

    public async Task WriteAsync(byte[] data, CancellationToken ct = default)
    {
        await _port.BaseStream.WriteAsync(data, ct);
        await _port.BaseStream.FlushAsync(ct);
    }

    public async Task<byte[]> ReadAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        var buffer = new byte[512];
        var totalRead = 0;

        try
        {
            // Устройства протокола «Меркурий»/Modbus RTU не передают явный признак конца кадра,
            // поэтому чтение продолжается, пока не истечёт межсимвольный тайм-аут (типично 1.5-3 симв. интервала,
            // здесь упрощённо — общий тайм-аут ответа).
            while (true)
            {
                var read = await _port.BaseStream.ReadAsync(buffer.AsMemory(totalRead), cts.Token);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (totalRead >= buffer.Length)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Тайм-аут ожидания ответа — нормальная ситуация, возвращаем то, что успели прочитать.
        }

        return buffer[..totalRead];
    }

    public ValueTask DisposeAsync()
    {
        if (_port.IsOpen)
        {
            _port.Close();
        }

        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}
