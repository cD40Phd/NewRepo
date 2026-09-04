using EnergyBalance.DataAccess.Repositories;
using EnergyBalance.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EnergyBalance.Acquisition.Polling;

public class PollingSchedulerOptions
{
    /// <summary>Максимальное число одновременных опросов приборов учёта (ограничение нагрузки на COM-порты/шлюзы).</summary>
    public int MaxDegreeOfParallelism { get; set; } = 8;
}

/// <summary>
/// Выполняет один цикл опроса текущих показаний по всем точкам учёта. Вызывается периодически
/// службой <c>EnergyBalance.Worker</c> (см. Workers/PollingWorker) по расписанию (например, раз в час).
/// Опрос отдельных приборов распараллеливается с ограничением, чтобы не перегружать
/// последовательные каналы связи и OPC-сервер одновременными запросами.
/// </summary>
public class PollingScheduler
{
    private readonly IMeteringPointRepository _meteringPointRepository;
    private readonly MeterDataImportService _importService;
    private readonly PollingSchedulerOptions _options;
    private readonly ILogger<PollingScheduler> _logger;

    public PollingScheduler(
        IMeteringPointRepository meteringPointRepository,
        MeterDataImportService importService,
        PollingSchedulerOptions options,
        ILogger<PollingScheduler> logger)
    {
        _meteringPointRepository = meteringPointRepository;
        _importService = importService;
        _options = options;
        _logger = logger;
    }

    /// <summary>Опрашивает текущие показания всех активных приборов учёта. Возвращает число успешных/неуспешных опросов.</summary>
    public async Task<(int Succeeded, int Failed)> RunCurrentValuesCycleAsync(CancellationToken ct = default)
    {
        var meteringPoints = await _meteringPointRepository.GetAllWithActiveMetersAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var activeMeters = meteringPoints
            .Select(mp => mp.GetActiveMeter(today))
            .Where(m => m is not null)
            .Cast<Meter>()
            .ToList();

        _logger.LogInformation("Запуск цикла опроса текущих показаний: {Count} приборов учёта", activeMeters.Count);

        var succeeded = 0;
        var failed = 0;
        using var throttle = new SemaphoreSlim(_options.MaxDegreeOfParallelism);

        var tasks = activeMeters.Select(async meter =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var ok = await _importService.PollCurrentValuesAsync(meter, ct);
                if (ok)
                {
                    Interlocked.Increment(ref succeeded);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation("Цикл опроса завершён: успешно {Succeeded}, с ошибками {Failed}", succeeded, failed);
        return (succeeded, failed);
    }
}
