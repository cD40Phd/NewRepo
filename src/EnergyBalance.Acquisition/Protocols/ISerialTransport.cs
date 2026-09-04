namespace EnergyBalance.Acquisition.Protocols;

/// <summary>
/// Абстракция физического канала связи (COM-порт или TCP-шлюз RS-485/RS-232),
/// позволяющая тестировать протокольные драйверы без реального оборудования.
/// </summary>
public interface ISerialTransport : IAsyncDisposable
{
    Task OpenAsync(CancellationToken ct = default);

    Task WriteAsync(byte[] data, CancellationToken ct = default);

    /// <summary>
    /// Читает ответный кадр. Реализация должна вернуть управление либо по получении
    /// ожидаемого количества байт/окончания кадра, либо по истечении таймаута.
    /// </summary>
    Task<byte[]> ReadAsync(TimeSpan timeout, CancellationToken ct = default);
}
