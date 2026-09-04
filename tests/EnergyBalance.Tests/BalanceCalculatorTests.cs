using EnergyBalance.BalanceEngine;
using EnergyBalance.BalanceEngine.Models;
using EnergyBalance.Domain.Entities;
using EnergyBalance.Domain.Enums;
using Xunit;

namespace EnergyBalance.Tests;

public class BalanceCalculatorTests
{
    private readonly BalanceGroupResolver _resolver = new();
    private readonly BalanceCalculator _calculator;

    public BalanceCalculatorTests()
    {
        _calculator = new BalanceCalculator(_resolver);
    }

    private static MeteringPoint CreatePoint(int id, decimal ratio = 1m) => new()
    {
        Id = id,
        Code = $"MP-{id}",
        Name = $"Точка учёта {id}",
        CurrentTransformerRatio = ratio,
        VoltageTransformerRatio = 1m,
    };

    private static BalanceGroupMember CreateMember(BalanceGroup group, MeteringPoint point, BalanceMemberRole role) => new()
    {
        BalanceGroup = group,
        BalanceGroupId = group.Id,
        MeteringPoint = point,
        MeteringPointId = point.Id,
        Role = role,
        EffectiveFrom = new DateOnly(2020, 1, 1),
    };

    [Fact]
    public void Calculate_BalancedGroup_ReturnsNormalStatus()
    {
        var group = new BalanceGroup { Id = 1, Name = "Фидер 1", Code = "F1", NormativeLossPercent = 2m, WarningThresholdPercent = 5m, CriticalThresholdPercent = 10m };
        var supplyPoint = CreatePoint(1);
        var consumptionPoint = CreatePoint(2);
        group.Members.Add(CreateMember(group, supplyPoint, BalanceMemberRole.Supply));
        group.Members.Add(CreateMember(group, consumptionPoint, BalanceMemberRole.Consumption));

        var energy = new Dictionary<int, MeteringPointEnergyInput>
        {
            [supplyPoint.Id] = new(supplyPoint.Id, 1000m, HasDataIssues: false),
            [consumptionPoint.Id] = new(consumptionPoint.Id, 980m, HasDataIssues: false), // 2% потерь = норматив
        };

        var periodStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddDays(1);

        var result = _calculator.Calculate(group, [group], periodStart, periodEnd, version: 1, energyByMeteringPoint: energy);

        Assert.Equal(1000m, result.SupplyEnergyKwh);
        Assert.Equal(980m, result.ConsumptionEnergyKwh);
        Assert.Equal(20m, result.NormativeLossKwh);
        Assert.Equal(20m, result.ActualLossKwh);
        Assert.Equal(0m, result.ImbalanceKwh);
        Assert.Equal(BalanceStatus.Normal, result.Status);
        Assert.Equal(2, result.MeteringPointsCount);
    }

    [Fact]
    public void Calculate_LargeImbalance_ReturnsCriticalStatus()
    {
        var group = new BalanceGroup { Id = 1, Name = "Фидер 1", Code = "F1", NormativeLossPercent = 2m, WarningThresholdPercent = 5m, CriticalThresholdPercent = 10m };
        var supplyPoint = CreatePoint(1);
        var consumptionPoint = CreatePoint(2);
        group.Members.Add(CreateMember(group, supplyPoint, BalanceMemberRole.Supply));
        group.Members.Add(CreateMember(group, consumptionPoint, BalanceMemberRole.Consumption));

        var energy = new Dictionary<int, MeteringPointEnergyInput>
        {
            [supplyPoint.Id] = new(supplyPoint.Id, 1000m, HasDataIssues: false),
            [consumptionPoint.Id] = new(consumptionPoint.Id, 700m, HasDataIssues: false), // 30% "потерь" — явное хищение/небаланс
        };

        var periodStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddDays(1);

        var result = _calculator.Calculate(group, [group], periodStart, periodEnd, version: 1, energyByMeteringPoint: energy);

        Assert.Equal(BalanceStatus.Critical, result.Status);
        Assert.True(result.ImbalancePercent > 10m);
    }

    [Fact]
    public void Calculate_AppliesTransformationRatio()
    {
        var group = new BalanceGroup { Id = 1, Name = "ПС 110/10", Code = "PS1" };
        var supplyPoint = CreatePoint(1, ratio: 100m); // Ктт=100
        group.Members.Add(CreateMember(group, supplyPoint, BalanceMemberRole.Supply));

        var energy = new Dictionary<int, MeteringPointEnergyInput>
        {
            [supplyPoint.Id] = new(supplyPoint.Id, 10m, HasDataIssues: false),
        };

        var periodStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = _calculator.Calculate(group, [group], periodStart, periodStart.AddDays(1), version: 1, energyByMeteringPoint: energy);

        Assert.Equal(1000m, result.SupplyEnergyKwh); // 10 * 100
    }

    [Fact]
    public void Calculate_MissingMeteringPointData_IsExcludedFromCount()
    {
        var group = new BalanceGroup { Id = 1, Name = "Фидер 1", Code = "F1" };
        var supplyPoint = CreatePoint(1);
        var pointWithoutData = CreatePoint(2);
        group.Members.Add(CreateMember(group, supplyPoint, BalanceMemberRole.Supply));
        group.Members.Add(CreateMember(group, pointWithoutData, BalanceMemberRole.Consumption));

        var energy = new Dictionary<int, MeteringPointEnergyInput>
        {
            [supplyPoint.Id] = new(supplyPoint.Id, 500m, HasDataIssues: false),
        };

        var periodStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = _calculator.Calculate(group, [group], periodStart, periodStart.AddDays(1), version: 1, energyByMeteringPoint: energy);

        Assert.Equal(1, result.MeteringPointsCount);
        Assert.Equal(500m, result.SupplyEnergyKwh);
        Assert.Equal(0m, result.ConsumptionEnergyKwh);
    }

    [Fact]
    public void Calculate_ParentGroup_IncludesChildGroupMembers()
    {
        var parent = new BalanceGroup { Id = 1, Name = "Компания", Code = "ROOT" };
        var child = new BalanceGroup { Id = 2, Name = "Фидер 1", Code = "F1", ParentGroupId = 1 };

        var supplyPoint = CreatePoint(1);
        var consumptionPoint = CreatePoint(2);
        child.Members.Add(CreateMember(child, supplyPoint, BalanceMemberRole.Supply));
        child.Members.Add(CreateMember(child, consumptionPoint, BalanceMemberRole.Consumption));

        var allGroups = new List<BalanceGroup> { parent, child };

        var energy = new Dictionary<int, MeteringPointEnergyInput>
        {
            [supplyPoint.Id] = new(supplyPoint.Id, 500m, HasDataIssues: false),
            [consumptionPoint.Id] = new(consumptionPoint.Id, 450m, HasDataIssues: false),
        };

        var periodStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var result = _calculator.Calculate(parent, allGroups, periodStart, periodStart.AddDays(1), version: 1, energyByMeteringPoint: energy);

        Assert.Equal(500m, result.SupplyEnergyKwh);
        Assert.Equal(450m, result.ConsumptionEnergyKwh);
    }

    [Fact]
    public void GetBottomUpOrder_ChildrenPrecedeParents()
    {
        var parent = new BalanceGroup { Id = 1, Name = "Компания", Code = "ROOT" };
        var child = new BalanceGroup { Id = 2, Name = "Фидер 1", Code = "F1", ParentGroupId = 1 };
        var grandchild = new BalanceGroup { Id = 3, Name = "ТП-1", Code = "TP1", ParentGroupId = 2 };

        var order = _resolver.GetBottomUpOrder([parent, child, grandchild]);

        var indexOfGrandchild = order.ToList().FindIndex(g => g.Id == 3);
        var indexOfChild = order.ToList().FindIndex(g => g.Id == 2);
        var indexOfParent = order.ToList().FindIndex(g => g.Id == 1);

        Assert.True(indexOfGrandchild < indexOfChild);
        Assert.True(indexOfChild < indexOfParent);
    }
}
