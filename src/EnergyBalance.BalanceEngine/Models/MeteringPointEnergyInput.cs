namespace EnergyBalance.BalanceEngine.Models;

/// <summary>
/// Суммарная энергия точки учёта за расчётный период (в первичных величинах, до умножения
/// на коэффициент трансформации) — исходные данные для <see cref="BalanceCalculator"/>,
/// подготавливаемые вызывающей стороной из архива показаний.
/// </summary>
/// <param name="MeteringPointId">Идентификатор точки учёта.</param>
/// <param name="ActiveEnergyKwh">Суммарная активная энергия за период, кВт·ч (первичная величина).</param>
/// <param name="HasDataIssues">
/// Признак того, что часть исходных интервалов была восстановлена (пропуски, интерполяция)
/// или помечена как сомнительная — используется для контроля полноты расчёта.
/// </param>
public record MeteringPointEnergyInput(int MeteringPointId, decimal ActiveEnergyKwh, bool HasDataIssues);
