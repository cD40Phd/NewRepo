namespace EnergyBalance.Acquisition.OpcIntegration;

/// <summary>
/// Соответствие измеряемых величин прибора учёта конкретным узлам (тегам) OPC-сервера
/// MasterOPC Insat / MasterSCADA 3.12. Для приборов с <see cref="Domain.Enums.MeterProtocol.OpcTag"/>
/// поле <c>Meter.PollingAddress</c> хранит базовый префикс тега (например, "PS1.F3.TU125"),
/// а конкретные суффиксы для каждой величины задаются здесь и настраиваются под фактическое
/// дерево тегов, экспортированное из MasterSCADA (см. п. 11.2 ТЗ).
/// </summary>
public class OpcTagMapping
{
    public string ActiveEnergyTariff1Suffix { get; init; } = ".ActiveEnergy.T1";
    public string ActiveEnergyTariff2Suffix { get; init; } = ".ActiveEnergy.T2";
    public string ActiveEnergyTariff3Suffix { get; init; } = ".ActiveEnergy.T3";
    public string ActiveEnergyTariff4Suffix { get; init; } = ".ActiveEnergy.T4";
    public string ReactiveEnergyTotalSuffix { get; init; } = ".ReactiveEnergy.Total";

    public string VoltageASuffix { get; init; } = ".Ua";
    public string VoltageBSuffix { get; init; } = ".Ub";
    public string VoltageCSuffix { get; init; } = ".Uc";
    public string CurrentASuffix { get; init; } = ".Ia";
    public string CurrentBSuffix { get; init; } = ".Ib";
    public string CurrentCSuffix { get; init; } = ".Ic";
    public string ActivePowerSuffix { get; init; } = ".P";
    public string PowerFactorSuffix { get; init; } = ".CosPhi";
    public string TamperFlagSuffix { get; init; } = ".Diag.CoverOpen";

    /// <summary>Префикс пространства имён узла OPC UA, например "ns=2;s=".</summary>
    public string NamespacePrefix { get; init; } = "ns=2;s=";

    public string BuildNodeId(string tagPrefix, string suffix) => $"{NamespacePrefix}{tagPrefix}{suffix}";

    public static OpcTagMapping Default { get; } = new();
}
