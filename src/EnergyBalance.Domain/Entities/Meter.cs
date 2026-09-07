using EnergyBalance.Domain.Enums;

namespace EnergyBalance.Domain.Entities;

/// <summary>
/// Физический прибор учёта, установленный на точке учёта в определённый период времени.
/// </summary>
public class Meter
{
    public int Id { get; set; }

    public int MeteringPointId { get; set; }
    public MeteringPoint? MeteringPoint { get; set; }

    /// <summary>Тип прибора учёта, например «Меркурий 230», «Меркурий 234», «Сириус 2-Л».</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Заводской (серийный) номер прибора.</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Номер поверительной пломбы прибора учёта.</summary>
    public string? SealNumber { get; set; }

    public MeterProtocol Protocol { get; set; }

    public CommunicationChannelType ChannelType { get; set; }

    /// <summary>
    /// Адрес опроса: для Меркурий/Modbus — сетевой адрес устройства на линии (1..255);
    /// для OpcTag — идентификатор узла/тега OPC-сервера.
    /// </summary>
    public string PollingAddress { get; set; } = string.Empty;

    /// <summary>
    /// Строка подключения к каналу связи: COM-порт (напр. "COM3", "/dev/ttyUSB0") или
    /// "host:port" для TCP-шлюза/OPC-эндпоинта.
    /// </summary>
    public string ConnectionEndpoint { get; set; } = string.Empty;

    /// <summary>Скорость обмена, бод (для последовательных протоколов).</summary>
    public int? BaudRate { get; set; }

    /// <summary>Пароль/ключ доступа к прибору (для протокола Меркурий), хранится зашифрованным на уровне DataAccess.</summary>
    public string? AccessPassword { get; set; }

    public DateOnly InstalledOn { get; set; }
    public DateOnly? RemovedOn { get; set; }

    public DateOnly? LastVerificationDate { get; set; }
    public DateOnly? NextVerificationDueDate { get; set; }

    public List<Reading> Readings { get; set; } = new();
    public List<LoadProfileReading> LoadProfileReadings { get; set; } = new();
}
