using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnergyBalance.DataAccess.Configurations;

public class FeederConfiguration : IEntityTypeConfiguration<Feeder>
{
    public void Configure(EntityTypeBuilder<Feeder> builder)
    {
        builder.ToTable("feeders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50);

        builder.HasMany(x => x.MeteringPoints)
            .WithOne(mp => mp.Feeder)
            .HasForeignKey(mp => mp.FeederId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
