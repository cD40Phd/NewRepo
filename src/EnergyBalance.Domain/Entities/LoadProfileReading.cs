using EnergyBalance.Domain.Enums;

namespace EnergyBalance.Domain.Entities;

/// <summary>
/// Значение получасового (либо часового — определяется настройкой прибора) профиля
/// нагрузки: приращение активной/реактивной энергии за интервал.
/// Основная по объёму таблица системы — кандидат на партиционирование/hypertable.
/// </summary>
public class LoadProfileReading
{
    public long Id { get; set; }

    public int MeterId { get; set; }
    public Meter? Meter { get; set; }

    /// <summary>Начало интервала (UTC).</summary>
    public DateTime IntervalStartUtc { get; set; }

    /// <summary>Длительность интервала, минут (обычно 30 или 60).</summary>
    public int IntervalMinutes { get; set; }

    /// <summary>Приращение активной энергии за интервал (в первичных величинах), кВт·ч.</summary>
    public decimal ActiveEnergyDelta { get; set; }

    /// <summary>Приращение реактивной энергии за интервал, кВАр·ч.</summary>
    public decimal? ReactiveEnergyDelta { get; set; }

    public ReadingQuality Quality { get; set; } = ReadingQuality.Good;

    public MeterProtocol Source { get; set; }
}
