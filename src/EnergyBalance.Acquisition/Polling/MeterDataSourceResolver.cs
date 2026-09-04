using EnergyBalance.Acquisition;
using EnergyBalance.Domain.Entities;
using EnergyBalance.Domain.Enums;

namespace EnergyBalance.Acquisition.Polling;

/// <summary>Выбирает подходящую реализацию <see cref="IMeterDataSource"/> для конкретного прибора учёта.</summary>
public class MeterDataSourceResolver
{
    private readonly IReadOnlyDictionary<MeterProtocol, IMeterDataSource> _sourcesByProtocol;

    public MeterDataSourceResolver(IEnumerable<IMeterDataSource> sources)
    {
        _sourcesByProtocol = sources.ToDictionary(s => s.Protocol);
    }

    public IMeterDataSource Resolve(Meter meter)
    {
        if (!_sourcesByProtocol.TryGetValue(meter.Protocol, out var source))
        {
            throw new InvalidOperationException($"Не зарегистрирован источник данных для протокола {meter.Protocol} (прибор {meter.SerialNumber}).");
        }

        return source;
    }
}
