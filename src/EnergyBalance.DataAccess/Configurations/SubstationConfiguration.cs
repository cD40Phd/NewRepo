using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnergyBalance.DataAccess.Configurations;

public class SubstationConfiguration : IEntityTypeConfiguration<Substation>
{
    public void Configure(EntityTypeBuilder<Substation> builder)
    {
        builder.ToTable("substations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50);
        builder.Property(x => x.VoltageClassKv).HasPrecision(10, 2);
        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasMany(x => x.Feeders)
            .WithOne(f => f.Substation)
            .HasForeignKey(f => f.SubstationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
