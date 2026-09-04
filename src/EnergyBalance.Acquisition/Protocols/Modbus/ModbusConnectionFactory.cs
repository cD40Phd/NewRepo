using System.IO.Ports;
using System.Net.Sockets;
using EnergyBalance.Domain.Entities;
using EnergyBalance.Domain.Enums;
using NModbus;
using NModbus.Serial;

namespace EnergyBalance.Acquisition.Protocols.Modbus;

public class ModbusConnectionFactory : IModbusConnectionFactory
{
    private static readonly ModbusFactory Factory = new();

    public Task<IModbusConnection> CreateAsync(Meter meter, CancellationToken ct = default)
    {
        IModbusConnection connection = meter.ChannelType switch
        {
            CommunicationChannelType.TcpGateway => CreateTcpConnection(meter),
            CommunicationChannelType.SerialRs485 or CommunicationChannelType.SerialRs232 => CreateSerialConnection(meter),
            _ => throw new InvalidOperationException($"Канал связи {meter.ChannelType} не поддерживается драйвером Modbus."),
        };

        return Task.FromResult(connection);
    }

    private static IModbusConnection CreateTcpConnection(Meter meter)
    {
        var parts = meter.ConnectionEndpoint.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
        {
            throw new InvalidOperationException($"Некорректный адрес TCP-шлюза: '{meter.ConnectionEndpoint}'. Ожидается формат host:port.");
        }

        var client = new TcpClient(parts[0], port);
        var master = Factory.CreateMaster(client);
        return new TcpModbusConnection(client, master);
    }

    private static IModbusConnection CreateSerialConnection(Meter meter)
    {
        var port = new SerialPort(meter.ConnectionEndpoint, meter.BaudRate ?? 9600, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 2000,
            WriteTimeout = 2000,
        };
        port.Open();
        var adapter = new SerialPortAdapter(port);
        var master = Factory.CreateRtuMaster(adapter);
        return new SerialModbusConnection(port, master);
    }

    private sealed class TcpModbusConnection : IModbusConnection
    {
        private readonly TcpClient _client;

        public TcpModbusConnection(TcpClient client, IModbusMaster master)
        {
            _client = client;
            Master = master;
        }

        public IModbusMaster Master { get; }

        public ValueTask DisposeAsync()
        {
            Master.Dispose();
            _client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SerialModbusConnection : IModbusConnection
    {
        private readonly SerialPort _port;

        public SerialModbusConnection(SerialPort port, IModbusMaster master)
        {
            _port = port;
            Master = master;
        }

        public IModbusMaster Master { get; }

        public ValueTask DisposeAsync()
        {
            Master.Dispose();
            if (_port.IsOpen)
            {
                _port.Close();
            }

            _port.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
