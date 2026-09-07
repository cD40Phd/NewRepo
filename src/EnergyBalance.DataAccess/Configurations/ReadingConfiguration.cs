using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnergyBalance.DataAccess.Configurations;

public class ReadingConfiguration : IEntityTypeConfiguration<Reading>
{
    public void Configure(EntityTypeBuilder<Reading> builder)
    {
        builder.ToTable("readings");
        builder.HasKey(x => x.Id);

        foreach (var propertyName in new[]
                 {
                     nameof(Reading.ActiveEnergyTariff1), nameof(Reading.ActiveEnergyTariff2),
                     nameof(Reading.ActiveEnergyTariff3), nameof(Reading.ActiveEnergyTariff4),
                     nameof(Reading.ActiveEnergyExportTariff1), nameof(Reading.ActiveEnergyExportTariff2),
                     nameof(Reading.ActiveEnergyExportTariff3), nameof(Reading.ActiveEnergyExportTariff4),
                     nameof(Reading.ReactiveEnergyImport), nameof(Reading.ReactiveEnergyExport),
                 })
        {
            builder.Property(propertyName).HasPrecision(18, 3);
        }

        builder.Property(x => x.VoltageA).HasPrecision(8, 2);
        builder.Property(x => x.VoltageB).HasPrecision(8, 2);
        builder.Property(x => x.VoltageC).HasPrecision(8, 2);
        builder.Property(x => x.CurrentA).HasPrecision(10, 3);
        builder.Property(x => x.CurrentB).HasPrecision(10, 3);
        builder.Property(x => x.CurrentC).HasPrecision(10, 3);
        builder.Property(x => x.ActivePower).HasPrecision(14, 3);
        builder.Property(x => x.PowerFactor).HasPrecision(4, 3);

        builder.Ignore(x => x.ActiveEnergyTotal);
        builder.Ignore(x => x.ActiveEnergyExportTotal);

        builder.HasIndex(x => new { x.MeterId, x.TimestampUtc }).IsUnique();
    }
}
