using EnergyBalance.BalanceEngine;
using EnergyBalance.BalanceEngine.Models;
using EnergyBalance.DataAccess.Repositories;
using EnergyBalance.Domain.Entities;
using EnergyBalance.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EnergyBalance.Application;

/// <summary>
/// Связывает хранилище данных (DataAccess) и чистое расчётное ядро (BalanceEngine):
/// подготавливает суммы энергии по точкам учёта за период и запускает расчёт по всем
/// балансовым группам в порядке «снизу вверх», сохраняя результат как новую версию.
/// Используется как из <c>EnergyBalance.Worker</c> (регламентный расчёт по расписанию),
/// так и из <c>EnergyBalance.Api</c> (расчёт по запросу пользователя).
/// </summary>
public class BalanceCalculationOrchestrator
{
    private readonly IBalanceGroupRepository _balanceGroupRepository;
    private readonly IMeteringPointRepository _meteringPointRepository;
    private readonly IReadingRepository _readingRepository;
    private readonly BalanceGroupResolver _resolver;
    private readonly BalanceCalculator _calculator;
    private readonly ILogger<BalanceCalculationOrchestrator> _logger;

    public BalanceCalculationOrchestrator(
        IBalanceGroupRepository balanceGroupRepository,
        IMeteringPointRepository meteringPointRepository,
        IReadingRepository readingRepository,
        BalanceGroupResolver resolver,
        BalanceCalculator calculator,
        ILogger<BalanceCalculationOrchestrator> logger)
    {
        _balanceGroupRepository = balanceGroupRepository;
        _meteringPointRepository = meteringPointRepository;
        _readingRepository = readingRepository;
        _resolver = resolver;
        _calculator = calculator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BalanceCalculationResult>> CalculateAllAsync(
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        string calculatedBy = "system",
        CancellationToken ct = default)
    {
        var allGroups = await _balanceGroupRepository.GetAllWithMembersAsync(ct);
        var energyByPoint = await BuildEnergyInputAsync(periodStartUtc, periodEndUtc, ct);

        var order = _resolver.GetBottomUpOrder(allGroups);
        var results = new List<BalanceCalculationResult>(order.Count);

        foreach (var group in order)
        {
            var version = await _balanceGroupRepository.GetNextVersionAsync(group.Id, periodStartUtc, periodEndUtc, ct);
            var result = _calculator.Calculate(group, allGroups, periodStartUtc, periodEndUtc, version, energyByPoint, calculatedBy);
            await _balanceGroupRepository.AddCalculationResultAsync(result, ct);

            if (result.Status != BalanceStatus.Normal)
            {
                _logger.LogWarning(
                    "Балансовая группа {GroupName} ({GroupCode}): небаланс {ImbalancePercent}% (статус {Status})",
                    group.Name, group.Code, result.ImbalancePercent, result.Status);
            }

            results.Add(result);
        }

        return results;
    }

    private async Task<Dictionary<int, MeteringPointEnergyInput>> BuildEnergyInputAsync(DateTime periodStartUtc, DateTime periodEndUtc, CancellationToken ct)
    {
        var meteringPoints = await _meteringPointRepository.GetAllWithActiveMetersAsync(ct);
        var periodDate = DateOnly.FromDateTime(periodStartUtc);
        var result = new Dictionary<int, MeteringPointEnergyInput>();

        foreach (var meteringPoint in meteringPoints)
        {
            var meter = meteringPoint.GetActiveMeter(periodDate);
            if (meter is null)
            {
                continue;
            }

            var profile = await _readingRepository.GetProfileAsync(meter.Id, periodStartUtc, periodEndUtc, ct);
            if (profile.Count == 0)
            {
                _logger.LogWarning("Нет данных профиля нагрузки для прибора {SerialNumber} за период {From}-{To}", meter.SerialNumber, periodStartUtc, periodEndUtc);
                continue;
            }

            var sum = profile.Sum(p => p.ActiveEnergyDelta);
            var hasIssues = profile.Any(p => p.Quality != ReadingQuality.Good);

            result[meteringPoint.Id] = new MeteringPointEnergyInput(meteringPoint.Id, sum, hasIssues);
        }

        return result;
    }
}
