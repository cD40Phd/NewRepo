using EnergyBalance.BalanceEngine;
using Microsoft.Extensions.DependencyInjection;

namespace EnergyBalance.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEnergyBalanceApplication(this IServiceCollection services)
    {
        services.AddSingleton<BalanceGroupResolver>();
        services.AddSingleton<BalanceCalculator>();
        services.AddScoped<BalanceCalculationOrchestrator>();
        return services;
    }
}
