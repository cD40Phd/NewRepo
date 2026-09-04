using EnergyBalance.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EnergyBalance.DataAccess;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEnergyBalanceDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("EnergyBalanceDb")
            ?? throw new InvalidOperationException("Connection string 'EnergyBalanceDb' is not configured.");

        var migrationsAssembly = typeof(EnergyBalanceDbContext).Assembly.FullName!;
        services.AddDbContext<EnergyBalanceDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(migrationsAssembly)));

        services.AddScoped<IReadingRepository, ReadingRepository>();
        services.AddScoped<IBalanceGroupRepository, BalanceGroupRepository>();
        services.AddScoped<IMeteringPointRepository, MeteringPointRepository>();

        return services;
    }
}
