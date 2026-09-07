using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnergyBalance.DataAccess.Configurations;

public class BalanceCalculationResultConfiguration : IEntityTypeConfiguration<BalanceCalculationResult>
{
    public void Configure(EntityTypeBuilder<BalanceCalculationResult> builder)
    {
        builder.ToTable("balance_calculation_results");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SupplyEnergyKwh).HasPrecision(18, 3);
        builder.Property(x => x.ConsumptionEnergyKwh).HasPrecision(18, 3);
        builder.Property(x => x.OwnNeedsEnergyKwh).HasPrecision(18, 3);
        builder.Property(x => x.NormativeLossKwh).HasPrecision(18, 3);
        builder.Property(x => x.CalculatedBy).HasMaxLength(100);

        builder.Ignore(x => x.ActualLossKwh);
        builder.Ignore(x => x.ImbalanceKwh);
        builder.Ignore(x => x.ImbalancePercent);

        builder.HasOne(x => x.BalanceGroup)
            .WithMany()
            .HasForeignKey(x => x.BalanceGroupId)
            .OnDelete(DeleteBehavior.Restrict);

        // Версии одного и того же периода/группы не должны дублироваться.
        builder.HasIndex(x => new { x.BalanceGroupId, x.PeriodStartUtc, x.PeriodEndUtc, x.Version }).IsUnique();
    }
}
