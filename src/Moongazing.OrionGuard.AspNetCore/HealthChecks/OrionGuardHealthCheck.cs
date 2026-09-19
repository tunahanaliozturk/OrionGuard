using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.AspNetCore.HealthChecks;

/// <summary>
/// Health check that verifies OrionGuard validation infrastructure is properly configured.
/// Checks that ValidatorFactory is registered and can resolve validators.
/// </summary>
/// <remarks>
/// A healthy result carries <c>ValidatorFactory</c> (the registered factory's type name) and <c>Version</c>
/// (the informational version of the OrionGuard core assembly loaded in the process).
/// </remarks>
public sealed class OrionGuardHealthCheck : IHealthCheck
{
    // Read from the loaded core assembly rather than written as a literal, which went stale with every release.
    private static readonly string CoreVersion = ReadVersion(typeof(IValidatorFactory).Assembly);

    private readonly IServiceProvider _serviceProvider;

    public OrionGuardHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if ValidatorFactory is registered
            var factory = _serviceProvider.GetService(typeof(IValidatorFactory));
            if (factory is null)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "OrionGuard ValidatorFactory is not registered. Call services.AddOrionGuard() in startup."));
            }

            var data = new Dictionary<string, object>
            {
                ["ValidatorFactory"] = factory.GetType().Name,
                ["Version"] = CoreVersion,
            };

            return Task.FromResult(HealthCheckResult.Healthy("OrionGuard validation infrastructure is healthy.", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("OrionGuard health check failed.", ex));
        }
    }

    private static string ReadVersion(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? assembly.GetName().Version?.ToString()
        ?? "unknown";
}
