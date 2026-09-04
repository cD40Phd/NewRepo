using EnergyBalance.Domain.Enums;

namespace EnergyBalance.Domain.Entities;

/// <summary>
/// Результат одного расчёта баланса за период для одной балансовой группы.
/// Пересчёт создаёт новую запись с увеличенным <see cref="Version"/> — существующие
/// результаты никогда не изменяются и не удаляются (требование трассируемости).
/// </summary>
public class BalanceCalculationResult
{
    public long Id { get; set; }

    public int BalanceGroupId { get; set; }
    public BalanceGroup? BalanceGroup { get; set; }

    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }

    /// <summary>Порядковый номер версии расчёта для данной группы и периода (1, 2, 3, ...).</summary>
    public int Version { get; set; }

    /// <summary>Суммарный отпуск (приход) электроэнергии в границы группы, кВт·ч (в расчётных величинах, с учётом Ктт/Ктн).</summary>
    public decimal SupplyEnergyKwh { get; set; }

    /// <summary>Суммарное потребление (расход) электроэнергии из группы, кВт·ч.</summary>
    public decimal ConsumptionEnergyKwh { get; set; }

    /// <summary>Нормативные (договорные) потери, кВт·ч.</summary>
    public decimal NormativeLossKwh { get; set; }

    /// <summary>Фактические потери = Отпуск − Потребление, кВт·ч.</summary>
    public decimal ActualLossKwh => SupplyEnergyKwh - ConsumptionEnergyKwh;

    /// <summary>Небаланс = Фактические потери − Нормативные потери, кВт·ч.</summary>
    public decimal ImbalanceKwh => ActualLossKwh - NormativeLossKwh;

    /// <summary>Относительный небаланс, % от отпуска.</summary>
    public decimal ImbalancePercent => SupplyEnergyKwh == 0m ? 0m : Math.Round(ImbalanceKwh / SupplyEnergyKwh * 100m, 3);

    public BalanceStatus Status { get; set; }

    /// <summary>Количество точек учёта, участвовавших в расчёте (для контроля полноты исходных данных).</summary>
    public int MeteringPointsCount { get; set; }

    /// <summary>Количество точек учёта с восстановленными/сомнительными данными за период.</summary>
    public int MeteringPointsWithIssuesCount { get; set; }

    public DateTime CalculatedAtUtc { get; set; }

    /// <summary>Логин/идентификатор пользователя (или "system" для автоматического расчёта по расписанию), инициировавшего расчёт.</summary>
    public string CalculatedBy { get; set; } = "system";

    /// <summary>Признак того, что расчёт официально утверждён ответственным инженером и является финальным для периода.</summary>
    public bool IsApproved { get; set; }
}
