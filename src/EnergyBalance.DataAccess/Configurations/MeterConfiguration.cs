using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnergyBalance.DataAccess.Configurations;

public class MeterConfiguration : IEntityTypeConfiguration<Meter>
{
    public void Configure(EntityTypeBuilder<Meter> builder)
    {
        builder.ToTable("meters");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ModelName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SerialNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SealNumber).HasMaxLength(50);
        builder.Property(x => x.PollingAddress).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ConnectionEndpoint).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AccessPassword).HasMaxLength(200);

        builder.HasIndex(x => new { x.SerialNumber, x.ModelName });
        builder.HasIndex(x => new { x.ConnectionEndpoint, x.PollingAddress });

        builder.HasMany(x => x.Readings)
            .WithOne(r => r.Meter)
            .HasForeignKey(r => r.MeterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.LoadProfileReadings)
            .WithOne(r => r.Meter)
            .HasForeignKey(r => r.MeterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
