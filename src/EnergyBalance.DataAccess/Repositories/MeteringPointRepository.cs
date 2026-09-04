using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnergyBalance.DataAccess.Repositories;

public class MeteringPointRepository : IMeteringPointRepository
{
    private readonly EnergyBalanceDbContext _db;

    public MeteringPointRepository(EnergyBalanceDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MeteringPoint>> GetAllWithActiveMetersAsync(CancellationToken ct = default) =>
        await _db.MeteringPoints
            .Include(mp => mp.Meters.Where(m => m.RemovedOn == null))
            .Include(mp => mp.Feeder)
            .AsSplitQuery()
            .ToListAsync(ct);

    public Task<MeteringPoint?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.MeteringPoints
            .Include(mp => mp.Meters)
            .FirstOrDefaultAsync(mp => mp.Id == id, ct);
}
