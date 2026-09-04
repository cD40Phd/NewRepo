using EnergyBalance.Domain.Entities;
using EnergyBalance.Domain.Enums;
using Opc.Ua;

namespace EnergyBalance.Acquisition.OpcIntegration;

/// <summary>
/// Основной канал сбора данных: чтение показаний из тегов, уже опрошенных MasterOPC Insat
/// и агрегированных в MasterSCADA 3.12, через клиент OPC UA (либо OPC DA-UA Wrapper,
/// штатно поставляемый с MasterSCADA, публикующий те же теги как эндпоинт OPC UA).
/// </summary>
public class MasterOpcClient : IMeterDataSource
{
    private readonly IOpcUaSessionProvider _sessionProvider;
    private readonly OpcTagMapping _tagMapping;

    public MasterOpcClient(IOpcUaSessionProvider sessionProvider, OpcTagMapping? tagMapping = null)
    {
        _sessionProvider = sessionProvider;
        _tagMapping = tagMapping ?? OpcTagMapping.Default;
    }

    public MeterProtocol Protocol => MeterProtocol.OpcTag;

    public async Task<MeterSnapshot> ReadCurrentValuesAsync(Meter meter, CancellationToken ct = default)
    {
        var session = await _sessionProvider.GetSessionAsync(ct);
        var tagPrefix = meter.PollingAddress;

        var suffixes = new[]
        {
            _tagMapping.ActiveEnergyTariff1Suffix,
            _tagMapping.ActiveEnergyTariff2Suffix,
            _tagMapping.ActiveEnergyTariff3Suffix,
            _tagMapping.ActiveEnergyTariff4Suffix,
            _tagMapping.ReactiveEnergyTotalSuffix,
            _tagMapping.VoltageASuffix,
            _tagMapping.VoltageBSuffix,
            _tagMapping.VoltageCSuffix,
            _tagMapping.CurrentASuffix,
            _tagMapping.CurrentBSuffix,
            _tagMapping.CurrentCSuffix,
            _tagMapping.ActivePowerSuffix,
            _tagMapping.PowerFactorSuffix,
            _tagMapping.TamperFlagSuffix,
        };

        var values = ReadNodeValues(session, tagPrefix, suffixes);

        return new MeterSnapshot(
            TimestampUtc: DateTime.UtcNow,
            ActiveEnergyTariff1: ToDecimal(values[0]),
            ActiveEnergyTariff2: ToDecimal(values[1]),
            ActiveEnergyTariff3: ToDecimal(values[2]),
            ActiveEnergyTariff4: ToDecimal(values[3]),
            ReactiveEnergyTotal: ToDecimal(values[4]),
            VoltageA: ToNullableDecimal(values[5]),
            VoltageB: ToNullableDecimal(values[6]),
            VoltageC: ToNullableDecimal(values[7]),
            CurrentA: ToNullableDecimal(values[8]),
            CurrentB: ToNullableDecimal(values[9]),
            CurrentC: ToNullableDecimal(values[10]),
            ActivePower: ToNullableDecimal(values[11]),
            PowerFactor: ToNullableDecimal(values[12]),
            TamperFlag: ToBool(values[13]));
    }

    public Task<IReadOnlyList<LoadProfileInterval>> ReadLoadProfileAsync(Meter meter, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        // Чтение исторического профиля нагрузки из MasterSCADA выполняется через сервис
        // OPC HDA (Historical Data Access) либо через прямой экспорт архива SCADA (CSV/SQL),
        // в зависимости от конфигурации хранения архивов у заказчика — реализуется на этапе
        // эскизного проектирования после получения доступа к архивам MasterSCADA (см. п. 11 ТЗ).
        throw new NotSupportedException(
            "Чтение профиля нагрузки через OPC UA требует доступа к серверу OPC HDA MasterSCADA " +
            "и настраивается индивидуально после получения доступа к архивам заказчика.");
    }

    private static object?[] ReadNodeValues(Opc.Ua.Client.Session session, string tagPrefix, IReadOnlyList<string> suffixes)
    {
        var nodesToRead = new ReadValueIdCollection();
        foreach (var suffix in suffixes)
        {
            nodesToRead.Add(new ReadValueId
            {
                NodeId = new NodeId(tagPrefix + suffix),
                AttributeId = Attributes.Value,
            });
        }

        session.Read(
            requestHeader: null,
            maxAge: 0,
            timestampsToReturn: TimestampsToReturn.Neither,
            nodesToRead: nodesToRead,
            results: out var results,
            diagnosticInfos: out _);

        return results.Select(r => StatusCode.IsGood(r.StatusCode) ? r.Value : null).ToArray();
    }

    private static decimal ToDecimal(object? value) => value is null ? 0m : Convert.ToDecimal(value);

    private static decimal? ToNullableDecimal(object? value) => value is null ? null : Convert.ToDecimal(value);

    private static bool ToBool(object? value) => value is not null && Convert.ToBoolean(value);
}
