namespace EnergyBalance.Acquisition;

/// <summary>
/// Сырой результат опроса текущих показаний прибора учёта, до сопоставления с доменной
/// сущностью <see cref="EnergyBalance.Domain.Entities.Reading"/> (сопоставление и сохранение
/// выполняет <see cref="MeterDataImportService"/>).
/// </summary>
public record MeterSnapshot(
    DateTime TimestampUtc,
    decimal ActiveEnergyTariff1,
    decimal ActiveEnergyTariff2,
    decimal ActiveEnergyTariff3,
    decimal ActiveEnergyTariff4,
    decimal ReactiveEnergyTotal,
    decimal? VoltageA,
    decimal? VoltageB,
    decimal? VoltageC,
    decimal? CurrentA,
    decimal? CurrentB,
    decimal? CurrentC,
    decimal? ActivePower,
    decimal? PowerFactor,
    bool TamperFlag);

/// <summary>Один интервал профиля нагрузки, полученный от прибора учёта.</summary>
public record LoadProfileInterval(
    DateTime IntervalStartUtc,
    int IntervalMinutes,
    decimal ActiveEnergyDelta,
    decimal? ReactiveEnergyDelta);
