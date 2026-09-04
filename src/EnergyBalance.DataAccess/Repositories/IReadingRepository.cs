using EnergyBalance.Domain.Entities;

namespace EnergyBalance.DataAccess.Repositories;

public interface IReadingRepository
{
    Task AddReadingAsync(Reading reading, CancellationToken ct = default);

    Task AddLoadProfileReadingsAsync(IEnumerable<LoadProfileReading> readings, CancellationToken ct = default);

    /// <summary>Последнее (по времени) показание прибора учёта.</summary>
    Task<Reading?> GetLatestReadingAsync(int meterId, CancellationToken ct = default);

    /// <summary>Сумма приращений профиля нагрузки прибора учёта за период [from, to).</summary>
    Task<decimal> SumActiveEnergyAsync(int meterId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<LoadProfileReading>> GetProfileAsync(int meterId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
