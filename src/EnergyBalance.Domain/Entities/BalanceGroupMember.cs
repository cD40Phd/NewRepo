using EnergyBalance.Domain.Enums;

namespace EnergyBalance.Domain.Entities;

/// <summary>
/// Связь точки учёта с балансовой группой, с указанием её роли (приход/расход)
/// и периода действия (для отслеживания истории переключений точек между группами).
/// </summary>
public class BalanceGroupMember
{
    public int Id { get; set; }

    public int BalanceGroupId { get; set; }
    public BalanceGroup? BalanceGroup { get; set; }

    public int MeteringPointId { get; set; }
    public MeteringPoint? MeteringPoint { get; set; }

    public BalanceMemberRole Role { get; set; }

    /// <summary>Дата начала действия привязки точки учёта к группе в данной роли.</summary>
    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Дата окончания действия (null — действует по настоящее время).</summary>
    public DateOnly? EffectiveTo { get; set; }
}
