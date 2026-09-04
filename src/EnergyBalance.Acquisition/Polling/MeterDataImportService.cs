using EnergyBalance.DataAccess.Repositories;
using EnergyBalance.Domain.Entities;
using EnergyBalance.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EnergyBalance.Acquisition.Polling;

/// <summary>
/// Опрашивает один прибор учёта через подходящий <see cref="IMeterDataSource"/> и сохраняет
/// результат как доменные сущности <see cref="Reading"/> / <see cref="LoadProfileReading"/>.
/// </summary>
public class MeterDataImportService
{
    private readonly MeterDataSourceResolver _resolver;
    private readonly IReadingRepository _readingRepository;
    private readonly ILogger<MeterDataImportService> _logger;

    public MeterDataImportService(MeterDataSourceResolver resolver, IReadingRepository readingRepository, ILogger<MeterDataImportService> logger)
    {
        _resolver = resolver;
        _readingRepository = readingRepository;
        _logger = logger;
    }

    public async Task<bool> PollCurrentValuesAsync(Meter meter, CancellationToken ct = default)
    {
        try
        {
            var source = _resolver.Resolve(meter);
            var snapshot = await source.ReadCurrentValuesAsync(meter, ct);

            var reading = new Reading
            {
                MeterId = meter.Id,
                TimestampUtc = snapshot.TimestampUtc,
                ActiveEnergyTariff1 = snapshot.ActiveEnergyTariff1,
                ActiveEnergyTariff2 = snapshot.ActiveEnergyTariff2,
                ActiveEnergyTariff3 = snapshot.ActiveEnergyTariff3,
                ActiveEnergyTariff4 = snapshot.ActiveEnergyTariff4,
                ReactiveEnergyTotal = snapshot.ReactiveEnergyTotal,
                VoltageA = snapshot.VoltageA,
                VoltageB = snapshot.VoltageB,
                VoltageC = snapshot.VoltageC,
                CurrentA = snapshot.CurrentA,
                CurrentB = snapshot.CurrentB,
                CurrentC = snapshot.CurrentC,
                ActivePower = snapshot.ActivePower,
                PowerFactor = snapshot.PowerFactor,
                TamperFlag = snapshot.TamperFlag,
                Quality = ReadingQuality.Good,
                Source = meter.Protocol,
            };

            await _readingRepository.AddReadingAsync(reading, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка опроса текущих показаний прибора {SerialNumber} (точка учёта {MeteringPointId})", meter.SerialNumber, meter.MeteringPointId);
            return false;
        }
    }

    public async Task<bool> PollLoadProfileAsync(Meter meter, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        try
        {
            var source = _resolver.Resolve(meter);
            var intervals = await source.ReadLoadProfileAsync(meter, fromUtc, toUtc, ct);

            var entities = intervals.Select(i => new LoadProfileReading
            {
                MeterId = meter.Id,
                IntervalStartUtc = i.IntervalStartUtc,
                IntervalMinutes = i.IntervalMinutes,
                ActiveEnergyDelta = i.ActiveEnergyDelta,
                ReactiveEnergyDelta = i.ReactiveEnergyDelta,
                Quality = ReadingQuality.Good,
                Source = meter.Protocol,
            });

            await _readingRepository.AddLoadProfileReadingsAsync(entities, ct);
            return true;
        }
        catch (NotSupportedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка опроса профиля нагрузки прибора {SerialNumber} за период {From}-{To}", meter.SerialNumber, fromUtc, toUtc);
            return false;
        }
    }
}
