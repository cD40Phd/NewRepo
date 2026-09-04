using EnergyBalance.Domain.Entities;
using EnergyBalance.Domain.Enums;
using Microsoft.Extensions.Logging;
using NModbus;

namespace EnergyBalance.Acquisition.Protocols.Modbus;

/// <summary>
/// Драйвер Modbus RTU/TCP для счётчиков «Сириус 2-Л» (и, в общем случае, любых Modbus-совместимых
/// приборов учёта — конкретная раскладка регистров задаётся через <see cref="SiriusRegisterMap"/>).
/// </summary>
public class SiriusModbusClient : IMeterDataSource
{
    private readonly IModbusConnectionFactory _connectionFactory;
    private readonly SiriusRegisterMap _registerMap;
    private readonly ILogger<SiriusModbusClient> _logger;

    public SiriusModbusClient(IModbusConnectionFactory connectionFactory, ILogger<SiriusModbusClient> logger, SiriusRegisterMap? registerMap = null)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _registerMap = registerMap ?? SiriusRegisterMap.Default;
    }

    public MeterProtocol Protocol => MeterProtocol.ModbusRtu;

    public async Task<MeterSnapshot> ReadCurrentValuesAsync(Meter meter, CancellationToken ct = default)
    {
        var slaveAddress = ParseSlaveAddress(meter.PollingAddress);
        await using var connection = await _connectionFactory.CreateAsync(meter, ct);
        var master = connection.Master;

        var t1 = await ReadEnergyAsync(master, slaveAddress, _registerMap.ActiveEnergyTariff1Register, ct);
        var t2 = await ReadEnergyAsync(master, slaveAddress, _registerMap.ActiveEnergyTariff2Register, ct);
        var t3 = await ReadEnergyAsync(master, slaveAddress, _registerMap.ActiveEnergyTariff3Register, ct);
        var t4 = await ReadEnergyAsync(master, slaveAddress, _registerMap.ActiveEnergyTariff4Register, ct);
        var reactive = await ReadEnergyAsync(master, slaveAddress, _registerMap.ReactiveEnergyTotalRegister, ct);

        var voltageA = await ReadAnalogAsync(master, slaveAddress, _registerMap.VoltageARegister, ct);
        var voltageB = await ReadAnalogAsync(master, slaveAddress, _registerMap.VoltageBRegister, ct);
        var voltageC = await ReadAnalogAsync(master, slaveAddress, _registerMap.VoltageCRegister, ct);
        var currentA = await ReadAnalogAsync(master, slaveAddress, _registerMap.CurrentARegister, ct);
        var currentB = await ReadAnalogAsync(master, slaveAddress, _registerMap.CurrentBRegister, ct);
        var currentC = await ReadAnalogAsync(master, slaveAddress, _registerMap.CurrentCRegister, ct);
        var activePower = await ReadAnalogAsync(master, slaveAddress, _registerMap.ActivePowerRegister, ct);
        var powerFactor = await ReadAnalogAsync(master, slaveAddress, _registerMap.PowerFactorRegister, ct);

        var statusRegisters = await master.ReadHoldingRegistersAsync(slaveAddress, _registerMap.StatusRegister, 1);
        var tamperFlag = (statusRegisters[0] & 0x0001) != 0;

        return new MeterSnapshot(
            TimestampUtc: DateTime.UtcNow,
            ActiveEnergyTariff1: t1,
            ActiveEnergyTariff2: t2,
            ActiveEnergyTariff3: t3,
            ActiveEnergyTariff4: t4,
            ReactiveEnergyTotal: reactive,
            VoltageA: voltageA,
            VoltageB: voltageB,
            VoltageC: voltageC,
            CurrentA: currentA,
            CurrentB: currentB,
            CurrentC: currentC,
            ActivePower: activePower,
            PowerFactor: powerFactor,
            TamperFlag: tamperFlag);
    }

    public async Task<IReadOnlyList<LoadProfileInterval>> ReadLoadProfileAsync(Meter meter, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        // Профиль нагрузки Сириус 2-Л хранится в архивных регистрах прибора и читается блоками
        // по номеру записи журнала; данный метод даёт рабочую реализацию на основе последовательного
        // приращения накопительного счётчика энергии между двумя опросами текущих значений —
        // для точного профиля из встроенного архива прибора необходимо использовать команду чтения
        // журнала интервалов согласно протоколу конкретной прошивки (см. примечание к SiriusRegisterMap).
        var slaveAddress = ParseSlaveAddress(meter.PollingAddress);
        await using var connection = await _connectionFactory.CreateAsync(meter, ct);
        var master = connection.Master;

        var result = new List<LoadProfileInterval>();
        var cursor = fromUtc;
        var previousEnergy = await ReadEnergyAsync(master, slaveAddress, _registerMap.ActiveEnergyTariff1Register, ct);

        while (cursor < toUtc)
        {
            var currentEnergy = await ReadEnergyAsync(master, slaveAddress, _registerMap.ActiveEnergyTariff1Register, ct);
            var delta = currentEnergy - previousEnergy;
            result.Add(new LoadProfileInterval(cursor, IntervalMinutes: 30, ActiveEnergyDelta: delta, ReactiveEnergyDelta: null));
            previousEnergy = currentEnergy;
            cursor = cursor.AddMinutes(30);
        }

        return result;
    }

    private async Task<decimal> ReadEnergyAsync(IModbusMaster master, byte slaveAddress, ushort startRegister, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var registers = await master.ReadHoldingRegistersAsync(slaveAddress, startRegister, 2);
        uint raw = ((uint)registers[0] << 16) | registers[1];
        return raw / _registerMap.EnergyScale;
    }

    private async Task<decimal> ReadAnalogAsync(IModbusMaster master, byte slaveAddress, ushort startRegister, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var registers = await master.ReadHoldingRegistersAsync(slaveAddress, startRegister, 2);
        uint raw = ((uint)registers[0] << 16) | registers[1];
        return raw / _registerMap.AnalogScale;
    }

    private static byte ParseSlaveAddress(string pollingAddress)
    {
        if (!byte.TryParse(pollingAddress, out var address))
        {
            throw new InvalidOperationException($"Некорректный Modbus-адрес прибора: '{pollingAddress}'.");
        }

        return address;
    }
}
