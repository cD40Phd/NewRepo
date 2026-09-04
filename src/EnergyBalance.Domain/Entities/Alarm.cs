using EnergyBalance.Domain.Enums;

namespace EnergyBalance.Domain.Entities;

/// <summary>Журнал событий системы: отказы опроса, расхождения каналов, критические небалансы и т.п.</summary>
public class Alarm
{
    public long Id { get; set; }

    public DateTime RaisedAtUtc { get; set; }
    public DateTime? ClearedAtUtc { get; set; }

    public AlarmSeverity Severity { get; set; }

    /// <summary>Код типа события, например "COMM_LOST", "CHANNEL_MISMATCH", "BALANCE_CRITICAL".</summary>
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Опциональная привязка к точке учёта, прибору или балансовой группе (для навигации из UI).</summary>
    public int? MeteringPointId { get; set; }
    public int? MeterId { get; set; }
    public int? BalanceGroupId { get; set; }

    public bool IsAcknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
}
