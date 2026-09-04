namespace EnergyBalance.Domain.Entities;

/// <summary>
/// Балансовая группа — узел иерархического дерева, для которого рассчитывается
/// баланс отпуска/потребления электроэнергии (может представлять компанию в целом,
/// подстанцию, фидер или произвольную группу точек поставки/потребителей).
/// </summary>
public class BalanceGroup
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    /// <summary>Родительская балансовая группа (null — корневая группа, например «Компания в целом»).</summary>
    public int? ParentGroupId { get; set; }
    public BalanceGroup? ParentGroup { get; set; }

    public List<BalanceGroup> ChildGroups { get; set; } = new();

    /// <summary>Норматив технологических потерь для данной группы, % от отпуска.</summary>
    public decimal NormativeLossPercent { get; set; }

    /// <summary>Порог предупредительного небаланса, %.</summary>
    public decimal WarningThresholdPercent { get; set; } = 5m;

    /// <summary>Порог критического небаланса, %.</summary>
    public decimal CriticalThresholdPercent { get; set; } = 10m;

    public List<BalanceGroupMember> Members { get; set; } = new();
}
