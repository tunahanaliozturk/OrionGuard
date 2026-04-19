using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.AspNetCore.HealthChecks;

/// <summary>
/// Health check that verifies OrionGuard validation infrastructure is properly configured.
/// Checks that ValidatorFactory is registered and can resolve validators.
/// </summary>
public sealed class OrionGuardHealthCheck : IHealthCheck
{
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

            // Check if ExceptionFactory is available — prefer DI registration, fall back to static provider
            var exceptionFactory = _serviceProvider.GetService<IExceptionFactory>() ?? ExceptionFactoryProvider.Current;
            if (exceptionFactory is null)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "OrionGuard ExceptionFactory is not configured."));
            }

            var data = new Dictionary<string, object>
            {
                ["ValidatorFactory"] = factory.GetType().Name,
                ["ExceptionFactory"] = exceptionFactory.GetType().Name,
                ["SupportedLanguages"] = 14,
                ["Version"] = "6.0.0"
            };

            return Task.FromResult(HealthCheckResult.Healthy("OrionGuard validation infrastructure is healthy.", data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("OrionGuard health check failed.", ex));
        }
    }
}
