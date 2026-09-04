namespace EnergyBalance.Acquisition.Protocols.Modbus;

/// <summary>
/// Карта регистров Modbus для счётчика «Сириус 2-Л». Значения адресов регистров по умолчанию
/// соответствуют типовой раскладке приборов данного семейства (32-битные величины в двух
/// последовательных holding-регистрах, big-endian по словам) и ДОЛЖНЫ быть уточнены по
/// действующей документации на прибор («Протокол Modbus. Сириус 2») на этапе пусконаладки —
/// см. п. 11 ТЗ (запрос карты регистров у заказчика).
/// </summary>
public class SiriusRegisterMap
{
    /// <summary>Начальный регистр накопленной активной энергии по тарифу 1 (2 регистра, 32 бита).</summary>
    public ushort ActiveEnergyTariff1Register { get; init; } = 0x0000;
    public ushort ActiveEnergyTariff2Register { get; init; } = 0x0002;
    public ushort ActiveEnergyTariff3Register { get; init; } = 0x0004;
    public ushort ActiveEnergyTariff4Register { get; init; } = 0x0006;

    public ushort ReactiveEnergyTotalRegister { get; init; } = 0x0008;

    public ushort VoltageARegister { get; init; } = 0x0010;
    public ushort VoltageBRegister { get; init; } = 0x0012;
    public ushort VoltageCRegister { get; init; } = 0x0014;

    public ushort CurrentARegister { get; init; } = 0x0016;
    public ushort CurrentBRegister { get; init; } = 0x0018;
    public ushort CurrentCRegister { get; init; } = 0x001A;

    public ushort ActivePowerRegister { get; init; } = 0x001C;
    public ushort PowerFactorRegister { get; init; } = 0x001E;

    /// <summary>Регистр статуса/диагностики (бит вскрытия корпуса и др.).</summary>
    public ushort StatusRegister { get; init; } = 0x0020;

    /// <summary>Масштабирующий коэффициент энергии: значение регистра / EnergyScale = кВт·ч.</summary>
    public decimal EnergyScale { get; init; } = 100m;

    /// <summary>Масштабирующий коэффициент напряжения/тока/мощности.</summary>
    public decimal AnalogScale { get; init; } = 100m;

    public static SiriusRegisterMap Default { get; } = new();
}
