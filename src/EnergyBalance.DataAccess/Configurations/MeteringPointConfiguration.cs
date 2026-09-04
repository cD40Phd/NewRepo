using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnergyBalance.DataAccess.Configurations;

public class MeteringPointConfiguration : IEntityTypeConfiguration<MeteringPoint>
{
    public void Configure(EntityTypeBuilder<MeteringPoint> builder)
    {
        builder.ToTable("metering_points");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();

        builder.Property(x => x.CurrentTransformerRatio).HasPrecision(12, 4);
        builder.Property(x => x.VoltageTransformerRatio).HasPrecision(12, 4);
        builder.Ignore(x => x.TransformationRatio);

        builder.HasMany(x => x.Meters)
            .WithOne(m => m.MeteringPoint)
            .HasForeignKey(m => m.MeteringPointId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
