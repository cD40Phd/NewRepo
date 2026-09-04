using EnergyBalance.Domain.Entities;

namespace EnergyBalance.DataAccess.Repositories;

public interface IBalanceGroupRepository
{
    /// <summary>Загружает все балансовые группы вместе с составом (точки учёта + роль), для построения дерева расчёта.</summary>
    Task<IReadOnlyList<BalanceGroup>> GetAllWithMembersAsync(CancellationToken ct = default);

    Task<BalanceGroup?> GetByIdWithMembersAsync(int id, CancellationToken ct = default);

    /// <summary>Следующий номер версии расчёта для группы и периода (1, если расчётов ещё не было).</summary>
    Task<int> GetNextVersionAsync(int balanceGroupId, DateTime periodStartUtc, DateTime periodEndUtc, CancellationToken ct = default);

    Task AddCalculationResultAsync(BalanceCalculationResult result, CancellationToken ct = default);
}
