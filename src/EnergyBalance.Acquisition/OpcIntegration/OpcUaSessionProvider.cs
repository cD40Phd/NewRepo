using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace EnergyBalance.Acquisition.OpcIntegration;

public class OpcUaClientOptions
{
    /// <summary>Адрес конечной точки OPC UA сервера, например "opc.tcp://scada-host:4840".
    /// Если MasterOPC предоставляет только OPC DA, используется штатный DA-UA Wrapper поставки MasterSCADA,
    /// который публикует те же теги как эндпоинт OPC UA.</summary>
    public string EndpointUrl { get; set; } = "opc.tcp://localhost:4840";

    public bool UseSecurity { get; set; } = false;

    public string ApplicationName { get; set; } = "EnergyBalance.Acquisition";
}

/// <summary>
/// Управляет жизненным циклом сессии OPC UA: создаёт конфигурацию приложения,
/// устанавливает соединение и переподключается при разрыве связи.
/// </summary>
public class OpcUaSessionProvider : IOpcUaSessionProvider
{
    private readonly OpcUaClientOptions _options;
    private readonly ILogger<OpcUaSessionProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Session? _session;

    public OpcUaSessionProvider(OpcUaClientOptions options, ILogger<OpcUaSessionProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<Session> GetSessionAsync(CancellationToken ct = default)
    {
        if (_session is { Connected: true })
        {
            return _session;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_session is { Connected: true })
            {
                return _session;
            }

            _session = await CreateSessionAsync(ct);
            return _session;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Session> CreateSessionAsync(CancellationToken ct)
    {
        _logger.LogInformation("Установка сессии OPC UA с {EndpointUrl}", _options.EndpointUrl);

        var applicationConfiguration = new ApplicationConfiguration
        {
            ApplicationName = _options.ApplicationName,
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = true,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 1024,
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
        };

        await applicationConfiguration.Validate(ApplicationType.Client);

        var endpointDescription = CoreClientUtils.SelectEndpoint(applicationConfiguration, _options.EndpointUrl, _options.UseSecurity, 15000);
        var endpointConfiguration = EndpointConfiguration.Create(applicationConfiguration);
        var configuredEndpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

        var session = await Session.Create(
            applicationConfiguration,
            configuredEndpoint,
            updateBeforeConnect: false,
            sessionName: _options.ApplicationName,
            sessionTimeout: 60000,
            identity: new UserIdentity(new AnonymousIdentityToken()),
            preferredLocales: null);

        session.KeepAlive += (_, e) =>
        {
            if (ServiceResult.IsBad(e.Status))
            {
                _logger.LogWarning("OPC UA keep-alive обнаружил проблему соединения: {Status}", e.Status);
            }
        };

        return session;
    }

    public ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            _session.Close();
            _session.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
