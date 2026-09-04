using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnergyBalance.DataAccess.Repositories;

public class ReadingRepository : IReadingRepository
{
    private readonly EnergyBalanceDbContext _db;

    public ReadingRepository(EnergyBalanceDbContext db)
    {
        _db = db;
    }

    public async Task AddReadingAsync(Reading reading, CancellationToken ct = default)
    {
        _db.Readings.Add(reading);
        await _db.SaveChangesAsync(ct);
    }

    public async Task AddLoadProfileReadingsAsync(IEnumerable<LoadProfileReading> readings, CancellationToken ct = default)
    {
        _db.LoadProfileReadings.AddRange(readings);
        await _db.SaveChangesAsync(ct);
    }

    public Task<Reading?> GetLatestReadingAsync(int meterId, CancellationToken ct = default) =>
        _db.Readings
            .Where(r => r.MeterId == meterId)
            .OrderByDescending(r => r.TimestampUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<decimal> SumActiveEnergyAsync(int meterId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var sum = await _db.LoadProfileReadings
            .Where(r => r.MeterId == meterId && r.IntervalStartUtc >= fromUtc && r.IntervalStartUtc < toUtc)
            .SumAsync(r => (decimal?)r.ActiveEnergyDelta, ct);
        return sum ?? 0m;
    }

    public async Task<IReadOnlyList<LoadProfileReading>> GetProfileAsync(int meterId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await _db.LoadProfileReadings
            .Where(r => r.MeterId == meterId && r.IntervalStartUtc >= fromUtc && r.IntervalStartUtc < toUtc)
            .OrderBy(r => r.IntervalStartUtc)
            .ToListAsync(ct);
}
