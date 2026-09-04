using Opc.Ua.Client;

namespace EnergyBalance.Acquisition.OpcIntegration;

/// <summary>
/// Предоставляет активную сессию OPC UA к серверу MasterOPC Insat / шлюзу MasterSCADA 3.12,
/// прозрачно переподключаясь при обрыве связи. Вынесено в отдельную абстракцию, чтобы
/// <see cref="MasterOpcClient"/> можно было тестировать без реального OPC-сервера.
/// </summary>
public interface IOpcUaSessionProvider : IAsyncDisposable
{
    Task<Session> GetSessionAsync(CancellationToken ct = default);
}
