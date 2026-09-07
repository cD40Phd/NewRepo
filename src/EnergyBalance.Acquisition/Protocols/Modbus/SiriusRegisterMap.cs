namespace EnergyBalance.Acquisition.Protocols.Modbus;

/// <summary>
/// Карта регистров Modbus для счётчика «Сириус 2-Л». Значения адресов регистров по умолчанию
/// соответствуют типовой раскладке приборов данного семейства (32-битные величины в двух
/// последовательных holding-регистрах, big-endian по словам) и ДОЛЖНЫ быть уточнены по
/// действующей документации на прибор («Протокол Modbus. Сириус 2») на этапе пусконаладки —
/// см. п. 11 ТЗ (запрос карты регистров у заказчика).
///
/// Раздельные регистры энергии «приём»/«выдача» (А+/А-) и реактивной энергии «приём»/«выдача»
/// (Р+/Р-) введены по аналогии с параметрами, подтверждёнными интерфейсом ПО «Пирамида 2.0»
/// (см. docs/METER_DATA_CONCLUSION.md) — большинство приборов на границах сети как минимум
/// резервируют под них место в карте регистров, даже если фактически всегда равны нулю.
/// </summary>
public class SiriusRegisterMap
{
    /// <summary>Начальный регистр накопленной активной энергии «приём» (А+) по тарифу 1 (2 регистра, 32 бита).</summary>
    public ushort ActiveEnergyTariff1Register { get; init; } = 0x0000;
    public ushort ActiveEnergyTariff2Register { get; init; } = 0x0002;
    public ushort ActiveEnergyTariff3Register { get; init; } = 0x0004;
    public ushort ActiveEnergyTariff4Register { get; init; } = 0x0006;

    /// <summary>Начальный регистр накопленной активной энергии «выдача» (А-) по тарифу 1.</summary>
    public ushort ActiveEnergyExportTariff1Register { get; init; } = 0x0008;
    public ushort ActiveEnergyExportTariff2Register { get; init; } = 0x000A;
    public ushort ActiveEnergyExportTariff3Register { get; init; } = 0x000C;
    public ushort ActiveEnergyExportTariff4Register { get; init; } = 0x000E;

    public ushort ReactiveEnergyImportRegister { get; init; } = 0x0010;
    public ushort ReactiveEnergyExportRegister { get; init; } = 0x0012;

    public ushort VoltageARegister { get; init; } = 0x0020;
    public ushort VoltageBRegister { get; init; } = 0x0022;
    public ushort VoltageCRegister { get; init; } = 0x0024;

    public ushort CurrentARegister { get; init; } = 0x0026;
    public ushort CurrentBRegister { get; init; } = 0x0028;
    public ushort CurrentCRegister { get; init; } = 0x002A;

    public ushort ActivePowerRegister { get; init; } = 0x002C;
    public ushort PowerFactorRegister { get; init; } = 0x002E;

    /// <summary>Регистр статуса/диагностики (бит вскрытия корпуса и др.).</summary>
    public ushort StatusRegister { get; init; } = 0x0030;

    /// <summary>Масштабирующий коэффициент энергии: значение регистра / EnergyScale = кВт·ч.</summary>
    public decimal EnergyScale { get; init; } = 100m;

    /// <summary>Масштабирующий коэффициент напряжения/тока/мощности.</summary>
    public decimal AnalogScale { get; init; } = 100m;

    public static SiriusRegisterMap Default { get; } = new();
}
