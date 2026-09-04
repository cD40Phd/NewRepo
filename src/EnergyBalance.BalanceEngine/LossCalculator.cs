using EnergyBalance.Domain.Enums;

namespace EnergyBalance.BalanceEngine;

/// <summary>Вспомогательные расчёты нормативных потерь и статуса небаланса.</summary>
public static class LossCalculator
{
    public static decimal ComputeNormativeLossKwh(decimal supplyEnergyKwh, decimal normativeLossPercent) =>
        Math.Round(supplyEnergyKwh * normativeLossPercent / 100m, 3);

    public static BalanceStatus DetermineStatus(decimal imbalancePercent, decimal warningThresholdPercent, decimal criticalThresholdPercent)
    {
        var absImbalance = Math.Abs(imbalancePercent);

        if (absImbalance >= criticalThresholdPercent)
        {
            return BalanceStatus.Critical;
        }

        if (absImbalance >= warningThresholdPercent)
        {
            return BalanceStatus.Warning;
        }

        return BalanceStatus.Normal;
    }
}
