using Microsoft.Extensions.DependencyInjection;

namespace Startup.HealthCheck;

public static class StartupHealthCheckExtensions
{
    public static IServiceCollection AddStartupHealthCheck(
        this IServiceCollection services,
        HealthCheckSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<StartupHealthCheck>();
        return services;
    }
}
