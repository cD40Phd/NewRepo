using EnergyBalance.Application;

namespace EnergyBalance.Worker.Workers;

/// <summary>
/// Регламентный (суточный) расчёт баланса за истёкшие сутки — запускается один раз в сутки
/// в настроенный час UTC (см. п. 6.3 ТЗ: расчёт коммерческого баланса не реже 1 раза в сутки).
/// </summary>
public class BalanceCalculationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BalanceCalculationWorker> _logger;

    public BalanceCalculationWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<BalanceCalculationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var targetHourUtc = _configuration.GetValue("Balance:DailyCalculationHourUtc", 1);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun(targetHourUtc);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunDailyCalculationAsync(stoppingToken);
        }
    }

    private async Task RunDailyCalculationAsync(CancellationToken ct)
    {
        var periodEnd = DateTime.UtcNow.Date;
        var periodStart = periodEnd.AddDays(-1);

        _logger.LogInformation("Запуск регламентного расчёта баланса за период {From} - {To}", periodStart, periodEnd);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<BalanceCalculationOrchestrator>();
            var results = await orchestrator.CalculateAllAsync(periodStart, periodEnd, "system-daily-schedule", ct);
            _logger.LogInformation("Регламентный расчёт баланса завершён: {Count} балансовых групп", results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка регламентного расчёта баланса за период {From} - {To}", periodStart, periodEnd);
        }
    }

    private static TimeSpan GetDelayUntilNextRun(int targetHourUtc)
    {
        var now = DateTime.UtcNow;
        var next = new DateTime(now.Year, now.Month, now.Day, targetHourUtc, 0, 0, DateTimeKind.Utc);
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }
}
