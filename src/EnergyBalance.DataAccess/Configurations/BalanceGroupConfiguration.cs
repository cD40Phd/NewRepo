using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnergyBalance.DataAccess.Configurations;

public class BalanceGroupConfiguration : IEntityTypeConfiguration<BalanceGroup>
{
    public void Configure(EntityTypeBuilder<BalanceGroup> builder)
    {
        builder.ToTable("balance_groups");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();

        builder.Property(x => x.NormativeLossPercent).HasPrecision(6, 3);
        builder.Property(x => x.WarningThresholdPercent).HasPrecision(6, 3);
        builder.Property(x => x.CriticalThresholdPercent).HasPrecision(6, 3);

        builder.HasOne(x => x.ParentGroup)
            .WithMany(x => x.ChildGroups)
            .HasForeignKey(x => x.ParentGroupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BalanceGroupMemberConfiguration : IEntityTypeConfiguration<BalanceGroupMember>
{
    public void Configure(EntityTypeBuilder<BalanceGroupMember> builder)
    {
        builder.ToTable("balance_group_members");
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.BalanceGroup)
            .WithMany(g => g.Members)
            .HasForeignKey(x => x.BalanceGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.MeteringPoint)
            .WithMany(mp => mp.BalanceGroupMemberships)
            .HasForeignKey(x => x.MeteringPointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.BalanceGroupId, x.MeteringPointId, x.EffectiveFrom });
    }
}
