using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.AspNetCore.ExceptionHandling;
using Moongazing.OrionGuard.AspNetCore.Filters;
using Moongazing.OrionGuard.AspNetCore.Options;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.AspNetCore.Extensions;

/// <summary>
/// Extension methods for registering OrionGuard ASP.NET Core services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OrionGuard ASP.NET Core integration services including validation filters,
    /// exception handling, and ProblemDetails support.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure <see cref="OrionGuardAspNetCoreOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> for further chaining.</returns>
    public static IServiceCollection AddOrionGuardAspNetCore(
        this IServiceCollection services,
        Action<OrionGuardAspNetCoreOptions>? configure = null)
    {
        services.AddOrionGuard();

        var options = new OrionGuardAspNetCoreOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddExceptionHandler<OrionGuardExceptionHandler>();
        services.AddProblemDetails();

        services.AddTransient<OrionGuardMvcFilter>();

        if (options.SuppressModelStateInvalidFilter)
        {
            services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
            {
                o.SuppressModelStateInvalidFilter = true;
            });
        }

        return services;
    }
}
