using EnergyBalance.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnergyBalance.DataAccess;

public class EnergyBalanceDbContext : DbContext
{
    public EnergyBalanceDbContext(DbContextOptions<EnergyBalanceDbContext> options) : base(options)
    {
    }

    public DbSet<Substation> Substations => Set<Substation>();
    public DbSet<Feeder> Feeders => Set<Feeder>();
    public DbSet<MeteringPoint> MeteringPoints => Set<MeteringPoint>();
    public DbSet<Meter> Meters => Set<Meter>();
    public DbSet<Reading> Readings => Set<Reading>();
    public DbSet<LoadProfileReading> LoadProfileReadings => Set<LoadProfileReading>();
    public DbSet<BalanceGroup> BalanceGroups => Set<BalanceGroup>();
    public DbSet<BalanceGroupMember> BalanceGroupMembers => Set<BalanceGroupMember>();
    public DbSet<BalanceCalculationResult> BalanceCalculationResults => Set<BalanceCalculationResult>();
    public DbSet<Alarm> Alarms => Set<Alarm>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EnergyBalanceDbContext).Assembly);
    }
}
