namespace EnergyBalance.Domain.Enums;

/// <summary>
/// Протокол обмена данными с прибором учёта либо канал получения данных.
/// </summary>
public enum MeterProtocol
{
    /// <summary>Фирменный протокол «Меркурий» (Меркурий 230 / 234), RS-485/RS-232.</summary>
    Mercury = 1,

    /// <summary>Modbus RTU/TCP (Сириус 2-Л и другие Modbus-совместимые приборы).</summary>
    ModbusRtu = 2,

    /// <summary>Данные получены не напрямую, а через тег OPC-сервера MasterOPC/MasterSCADA.</summary>
    OpcTag = 3,
}

/// <summary>Физический канал связи с прибором учёта.</summary>
public enum CommunicationChannelType
{
    SerialRs485 = 1,
    SerialRs232 = 2,
    TcpGateway = 3,
    OpcServer = 4,
}
