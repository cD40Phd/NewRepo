namespace EnergyBalance.Api.Dto;

public record MeteringPointDto(
    int Id,
    string Code,
    string Name,
    string? FeederName,
    decimal TransformationRatio,
    string? ActiveMeterModel,
    string? ActiveMeterSerialNumber);

public record ReadingDto(
    DateTime TimestampUtc,
    decimal ActiveEnergyTotal,
    decimal ActiveEnergyExportTotal,
    decimal? VoltageA,
    decimal? VoltageB,
    decimal? VoltageC,
    decimal? ActivePower,
    decimal? PowerFactor,
    string Quality,
    bool TamperFlag);

public record LoadProfilePointDto(DateTime IntervalStartUtc, int IntervalMinutes, decimal ActiveEnergyDelta, decimal ActiveEnergyExportDelta, string Quality);

public record BalanceCalculationResultDto(
    long Id,
    int BalanceGroupId,
    string BalanceGroupName,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    int Version,
    decimal SupplyEnergyKwh,
    decimal ConsumptionEnergyKwh,
    decimal OwnNeedsEnergyKwh,
    decimal NormativeLossKwh,
    decimal ActualLossKwh,
    decimal ImbalanceKwh,
    decimal ImbalancePercent,
    string Status,
    int MeteringPointsCount,
    int MeteringPointsWithIssuesCount,
    DateTime CalculatedAtUtc,
    string CalculatedBy,
    bool IsApproved);

public record CalculateBalanceRequestDto(DateTime PeriodStartUtc, DateTime PeriodEndUtc);
