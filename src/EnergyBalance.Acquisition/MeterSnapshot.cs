namespace EnergyBalance.Acquisition;

/// <summary>
/// Сырой результат опроса текущих показаний прибора учёта, до сопоставления с доменной
/// сущностью <see cref="EnergyBalance.Domain.Entities.Reading"/> (сопоставление и сохранение
/// выполняет <see cref="MeterDataImportService"/>). Энергия «выдача» (А-) и обе составляющие
/// реактивной энергии (Р+/Р-) обязательны к заполнению драйвером (нулём — если прибор/точка
/// однонаправленные) по аналогии с параметрами «Энергия A-», «Энергия P+», «Энергия P-»
/// в интерфейсе ПО «Пирамида 2.0».
/// </summary>
public record MeterSnapshot(
    DateTime TimestampUtc,
    decimal ActiveEnergyTariff1,
    decimal ActiveEnergyTariff2,
    decimal ActiveEnergyTariff3,
    decimal ActiveEnergyTariff4,
    decimal ActiveEnergyExportTariff1,
    decimal ActiveEnergyExportTariff2,
    decimal ActiveEnergyExportTariff3,
    decimal ActiveEnergyExportTariff4,
    decimal ReactiveEnergyImport,
    decimal ReactiveEnergyExport,
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
    decimal ActiveEnergyExportDelta,
    decimal? ReactiveEnergyImportDelta,
    decimal? ReactiveEnergyExportDelta);
