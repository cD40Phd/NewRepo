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

    public List<Meter> Meters { get; set; } = new();

    public List<BalanceGroupMember> BalanceGroupMemberships { get; set; } = new();

    /// <summary>Действующий на текущий момент прибор учёта (последний по дате установки без даты снятия).</summary>
    public Meter? GetActiveMeter(DateOnly onDate) =>
        Meters
            .Where(m => m.InstalledOn <= onDate && (m.RemovedOn is null || m.RemovedOn > onDate))
            .OrderByDescending(m => m.InstalledOn)
            .FirstOrDefault();
}
