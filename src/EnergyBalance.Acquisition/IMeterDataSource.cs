using EnergyBalance.Domain.Entities;
using EnergyBalance.Domain.Enums;

namespace EnergyBalance.Acquisition;

/// <summary>
/// Единая абстракция источника данных прибора учёта — реализуется как для прямых
/// протокольных драйверов (Меркурий, Modbus), так и для чтения тегов OPC-сервера.
/// Позволяет планировщику опроса (<see cref="Polling.PollingScheduler"/>) работать со всеми
/// источниками одинаково и упрощает добавление новых протоколов в будущем.
/// </summary>
public interface IMeterDataSource
{
    /// <summary>Протокол, обслуживаемый данной реализацией.</summary>
    MeterProtocol Protocol { get; }

    /// <summary>Признак того, что источник умеет отвечать за данный прибор учёта.</summary>
    bool CanHandle(Meter meter) => meter.Protocol == Protocol;

    /// <summary>Считать текущий (мгновенный) срез показаний.</summary>
    Task<MeterSnapshot> ReadCurrentValuesAsync(Meter meter, CancellationToken ct = default);

    /// <summary>Считать профиль нагрузки за период [fromUtc, toUtc).</summary>
    Task<IReadOnlyList<LoadProfileInterval>> ReadLoadProfileAsync(Meter meter, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
