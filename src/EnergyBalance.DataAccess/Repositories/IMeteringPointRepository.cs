using EnergyBalance.Domain.Entities;

namespace EnergyBalance.DataAccess.Repositories;

public interface IMeteringPointRepository
{
    Task<IReadOnlyList<MeteringPoint>> GetAllWithActiveMetersAsync(CancellationToken ct = default);

    Task<MeteringPoint?> GetByIdAsync(int id, CancellationToken ct = default);
}
