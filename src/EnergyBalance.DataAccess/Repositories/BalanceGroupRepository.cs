using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnergyBalance.DataAccess.Repositories;

public class BalanceGroupRepository : IBalanceGroupRepository
{
    private readonly EnergyBalanceDbContext _db;

    public BalanceGroupRepository(EnergyBalanceDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BalanceGroup>> GetAllWithMembersAsync(CancellationToken ct = default) =>
        await _db.BalanceGroups
            .Include(g => g.Members)
                .ThenInclude(m => m.MeteringPoint)
            .AsSplitQuery()
            .ToListAsync(ct);

    public Task<BalanceGroup?> GetByIdWithMembersAsync(int id, CancellationToken ct = default) =>
        _db.BalanceGroups
            .Include(g => g.Members)
                .ThenInclude(m => m.MeteringPoint)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<int> GetNextVersionAsync(int balanceGroupId, DateTime periodStartUtc, DateTime periodEndUtc, CancellationToken ct = default)
    {
        var maxVersion = await _db.BalanceCalculationResults
            .Where(r => r.BalanceGroupId == balanceGroupId
                        && r.PeriodStartUtc == periodStartUtc
                        && r.PeriodEndUtc == periodEndUtc)
            .Select(r => (int?)r.Version)
            .MaxAsync(ct);

        return (maxVersion ?? 0) + 1;
    }

    public async Task AddCalculationResultAsync(BalanceCalculationResult result, CancellationToken ct = default)
    {
        _db.BalanceCalculationResults.Add(result);
        await _db.SaveChangesAsync(ct);
    }
}
