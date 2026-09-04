using EnergyBalance.Acquisition.Polling;
using Microsoft.Extensions.Configuration;

namespace EnergyBalance.Worker.Workers;

/// <summary>
/// Периодически запускает цикл опроса текущих показаний всех точек учёта
/// (см. <see cref="PollingScheduler"/>). Интервал задаётся настройкой
/// "Polling:CurrentValuesIntervalMinutes" (по умолчанию — раз в час, см. п. 5.4 ТЗ).
/// </summary>
public class PollingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PollingWorker> _logger;

    public PollingWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<PollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = _configuration.GetValue("Polling:CurrentValuesIntervalMinutes", 60);
        var interval = TimeSpan.FromMinutes(intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var scheduler = scope.ServiceProvider.GetRequiredService<PollingScheduler>();
                await scheduler.RunCurrentValuesCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка выполнения цикла опроса приборов учёта");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
