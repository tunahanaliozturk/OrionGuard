using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Blazor.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers OrionGuard Blazor validation services.
    /// </summary>
    public static IServiceCollection AddOrionGuardBlazor(this IServiceCollection services)
    {
        // Register core OrionGuard if not already registered
        services.AddOrionGuard();
        return services;
    }
}
