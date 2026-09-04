using EnergyBalance.BalanceEngine.Models;
using EnergyBalance.Domain.Entities;
using EnergyBalance.Domain.Enums;

namespace EnergyBalance.BalanceEngine;

/// <summary>
/// Расчётное ядро баланса отпуска и потребления электроэнергии. Чистая логика без внешних
/// зависимостей (БД, сеть) — исходные суммы энергии по точкам учёта подготавливает вызывающая
/// сторона (см. <see cref="MeteringPointEnergyInput"/>), что делает ядро полностью тестируемым.
/// </summary>
public class BalanceCalculator
{
    private readonly BalanceGroupResolver _resolver;

    public BalanceCalculator(BalanceGroupResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// Рассчитывает баланс для одной группы за период. Точки учёта, для которых не найдено
    /// исходных данных в <paramref name="energyByMeteringPoint"/>, исключаются из расчёта
    /// (не считаются нулевыми) — это отражается в <see cref="BalanceCalculationResult.MeteringPointsCount"/>
    /// относительно фактического состава группы.
    /// </summary>
    public BalanceCalculationResult Calculate(
        BalanceGroup group,
        IReadOnlyCollection<BalanceGroup> allGroups,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        int version,
        IReadOnlyDictionary<int, MeteringPointEnergyInput> energyByMeteringPoint,
        string calculatedBy = "system")
    {
        var members = _resolver.GetEffectiveMembers(group, allGroups);

        decimal supply = 0m;
        decimal consumption = 0m;
        var pointsCount = 0;
        var issuesCount = 0;

        foreach (var member in members)
        {
            if (!IsActiveDuring(member, periodStartUtc, periodEndUtc))
            {
                continue;
            }

            if (!energyByMeteringPoint.TryGetValue(member.MeteringPointId, out var energyInput))
            {
                continue;
            }

            var ratio = member.MeteringPoint?.TransformationRatio ?? 1m;
            var value = energyInput.ActiveEnergyKwh * ratio;

            pointsCount++;
            if (energyInput.HasDataIssues)
            {
                issuesCount++;
            }

            switch (member.Role)
            {
                case BalanceMemberRole.Supply:
                    supply += value;
                    break;
                case BalanceMemberRole.Consumption:
                    consumption += value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(member.Role), member.Role, "Неизвестная роль точки учёта в балансовой группе.");
            }
        }

        var normativeLoss = LossCalculator.ComputeNormativeLossKwh(supply, group.NormativeLossPercent);

        var result = new BalanceCalculationResult
        {
            BalanceGroupId = group.Id,
            PeriodStartUtc = periodStartUtc,
            PeriodEndUtc = periodEndUtc,
            Version = version,
            SupplyEnergyKwh = Math.Round(supply, 3),
            ConsumptionEnergyKwh = Math.Round(consumption, 3),
            NormativeLossKwh = normativeLoss,
            MeteringPointsCount = pointsCount,
            MeteringPointsWithIssuesCount = issuesCount,
            CalculatedAtUtc = DateTime.UtcNow,
            CalculatedBy = calculatedBy,
        };

        result.Status = LossCalculator.DetermineStatus(result.ImbalancePercent, group.WarningThresholdPercent, group.CriticalThresholdPercent);

        return result;
    }

    private static bool IsActiveDuring(BalanceGroupMember member, DateTime periodStartUtc, DateTime periodEndUtc)
    {
        var effectiveFrom = member.EffectiveFrom.ToDateTime(TimeOnly.MinValue);
        if (effectiveFrom >= periodEndUtc)
        {
            return false;
        }

        if (member.EffectiveTo is { } effectiveTo && effectiveTo.ToDateTime(TimeOnly.MinValue) <= periodStartUtc)
        {
            return false;
        }

        return true;
    }
}
