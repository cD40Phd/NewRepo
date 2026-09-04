using EnergyBalance.Acquisition.OpcIntegration;
using EnergyBalance.Acquisition.Polling;
using EnergyBalance.Acquisition.Protocols;
using EnergyBalance.Acquisition.Protocols.Mercury;
using EnergyBalance.Acquisition.Protocols.Modbus;
using EnergyBalance.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnergyBalance.Acquisition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEnergyBalanceAcquisition(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new OpcUaClientOptions
        {
            EndpointUrl = configuration["Opc:EndpointUrl"] ?? "opc.tcp://localhost:4840",
            UseSecurity = bool.TryParse(configuration["Opc:UseSecurity"], out var useSecurity) && useSecurity,
        });
        services.AddSingleton<IOpcUaSessionProvider, OpcUaSessionProvider>();
        services.AddSingleton(OpcTagMapping.Default);
        services.AddSingleton(SiriusRegisterMap.Default);
        services.AddSingleton<IModbusConnectionFactory, ModbusConnectionFactory>();

        // Транспорт для протокола "Меркурий" создаётся индивидуально на каждый вызов
        // (одно физическое соединение не может обслуживать параллельные сессии с разными приборами).
        services.AddSingleton<Func<Meter, ISerialTransport>>(_ => meter =>
            new SerialPortTransport(meter.ConnectionEndpoint, meter.BaudRate ?? 9600));

        services.AddSingleton<IMeterDataSource, MercuryProtocolClient>();
        services.AddSingleton<IMeterDataSource, SiriusModbusClient>();
        services.AddSingleton<IMeterDataSource, MasterOpcClient>();

        services.AddSingleton<MeterDataSourceResolver>();
        services.AddScoped<MeterDataImportService>();
        services.AddSingleton(new PollingSchedulerOptions
        {
            MaxDegreeOfParallelism = int.TryParse(configuration["Polling:MaxDegreeOfParallelism"], out var dop) ? dop : 8,
        });
        services.AddScoped<PollingScheduler>();

        return services;
    }
}
