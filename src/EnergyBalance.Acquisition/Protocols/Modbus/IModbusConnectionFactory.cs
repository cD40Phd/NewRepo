using EnergyBalance.Domain.Entities;
using NModbus;

namespace EnergyBalance.Acquisition.Protocols.Modbus;

/// <summary>Открытое соединение Modbus вместе с ведущим устройством (master), закрывается при Dispose.</summary>
public interface IModbusConnection : IAsyncDisposable
{
    IModbusMaster Master { get; }
}

/// <summary>
/// Создаёт соединение Modbus (RTU через COM-порт или TCP через Ethernet-шлюз) в зависимости
/// от настроек прибора учёта (<see cref="Meter.ChannelType"/>, <see cref="Meter.ConnectionEndpoint"/>).
/// </summary>
public interface IModbusConnectionFactory
{
    Task<IModbusConnection> CreateAsync(Meter meter, CancellationToken ct = default);
}
