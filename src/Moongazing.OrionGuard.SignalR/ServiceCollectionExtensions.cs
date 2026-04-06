using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Moongazing.OrionGuard.SignalR;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OrionGuard SignalR validation hub filter.
    /// Usage: services.AddOrionGuardSignalR();
    /// </summary>
    public static IServiceCollection AddOrionGuardSignalR(this IServiceCollection services)
    {
        services.AddSingleton<OrionGuardHubFilter>();
        services.AddSignalR(options =>
        {
            options.AddFilter<OrionGuardHubFilter>();
        });
        return services;
    }
}
