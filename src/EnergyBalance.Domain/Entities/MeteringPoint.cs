namespace EnergyBalance.Domain.Entities;

/// <summary>
/// Точка учёта электроэнергии — граница балансовой принадлежности, к которой привязан один
/// действующий прибор учёта (при замене ПУ создаётся новая запись <see cref="Meter"/>,
/// а точка учёта сохраняется как неизменный во времени объект НСИ).
/// </summary>
public class MeteringPoint
{
    public int Id { get; set; }

    /// <summary>Наименование/адрес точки учёта.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Уникальный код точки учёта (используется в отчётах и балансовых актах).</summary>
    public string Code { get; set; } = string.Empty;

    public int? FeederId { get; set; }
    public Feeder? Feeder { get; set; }

    /// <summary>Коэффициент трансформации тока (Ктт).</summary>
    public decimal CurrentTransformerRatio { get; set; } = 1m;

    /// <summary>Коэффициент трансформации напряжения (Ктн).</summary>
    public decimal VoltageTransformerRatio { get; set; } = 1m;

    /// <summary>Суммарный расчётный коэффициент трансформации (Ктт × Ктн).</summary>
    public decimal TransformationRatio => CurrentTransformerRatio * VoltageTransformerRatio;

    /// <summary>Класс точности измерительного трансформатора тока (например, "0.2S", "0.5").</summary>
    public string? CurrentTransformerAccuracyClass { get; set; }

    /// <summary>Класс точности измерительного трансформатора напряжения.</summary>
    public string? VoltageTransformerAccuracyClass { get; set; }

    /// <summary>
    /// Максимальная заявленная мощность по условиям договора присоединения, кВт. Используется
    /// для контроля превышения (см. интерфейс «Фактическая и резервируемая мощности» ПО
    /// «Пирамида 2.0») — не является исходными данными для расчёта энергетического баланса,
    /// но обязательна для контроля мощностного баланса точки.
    /// </summary>
    public decimal? MaxDeclaredPowerKw { get; set; }

    /// <summary>Максимально допустимый по условиям договора коэффициент реактивной мощности tg(φ).</summary>
    public decimal? MaxReactivePowerCoefficient { get; set; }

    /// <summary>Текущий установленный лимит мощности прибора учёта, кВт (если прибор поддерживает функцию реле-ограничителя).</summary>
    public decimal? PowerLimitKw { get; set; }

    public List<Meter> Meters { get; set; } = new();

    public List<BalanceGroupMember> BalanceGroupMemberships { get; set; } = new();

    /// <summary>Действующий на текущий момент прибор учёта (последний по дате установки без даты снятия).</summary>
    public Meter? GetActiveMeter(DateOnly onDate) =>
        Meters
            .Where(m => m.InstalledOn <= onDate && (m.RemovedOn is null || m.RemovedOn > onDate))
            .OrderByDescending(m => m.InstalledOn)
            .FirstOrDefault();
}
