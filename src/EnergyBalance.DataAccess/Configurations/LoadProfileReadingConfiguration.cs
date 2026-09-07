using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnergyBalance.DataAccess.Configurations;

public class LoadProfileReadingConfiguration : IEntityTypeConfiguration<LoadProfileReading>
{
    public void Configure(EntityTypeBuilder<LoadProfileReading> builder)
    {
        // ПРИМЕЧАНИЕ: в продуктивном развёртывании с TimescaleDB эта таблица должна быть
        // преобразована в hypertable командой SELECT create_hypertable('load_profile_readings', 'interval_start_utc');
        // см. sql/schema.sql.
        builder.ToTable("load_profile_readings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActiveEnergyDelta).HasPrecision(18, 3);
        builder.Property(x => x.ActiveEnergyExportDelta).HasPrecision(18, 3);
        builder.Property(x => x.ReactiveEnergyImportDelta).HasPrecision(18, 3);
        builder.Property(x => x.ReactiveEnergyExportDelta).HasPrecision(18, 3);

        builder.HasIndex(x => new { x.MeterId, x.IntervalStartUtc }).IsUnique();
    }
}
