using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moongazing.OrionGuard.AspNetCore.HealthChecks;

namespace Moongazing.OrionGuard.AspNetCore.Extensions;

/// <summary>
/// Extension methods for adding OrionGuard health checks to the health check pipeline.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds an OrionGuard health check that verifies validation infrastructure is properly configured.
    /// <para>
    /// Usage: <c>services.AddHealthChecks().AddOrionGuardCheck();</c>
    /// </para>
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The name of the health check. Defaults to "orionguard".</param>
    /// <param name="failureStatus">The <see cref="HealthStatus"/> to report when the check fails. Defaults to <see langword="null"/> (uses system default).</param>
    /// <param name="tags">Optional tags for filtering health checks.</param>
    /// <returns>The <see cref="IHealthChecksBuilder"/> for chaining.</returns>
    public static IHealthChecksBuilder AddOrionGuardCheck(
        this IHealthChecksBuilder builder,
        string name = "orionguard",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null)
    {
        return builder.AddCheck<OrionGuardHealthCheck>(
            name,
            failureStatus,
            tags ?? new[] { "validation", "orionguard" });
    }
}
