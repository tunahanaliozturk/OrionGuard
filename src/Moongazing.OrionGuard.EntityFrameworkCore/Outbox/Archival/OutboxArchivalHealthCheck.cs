namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

/// <summary>
/// <see cref="IHealthCheck"/> that watches the
/// <see cref="OutboxArchivalHostedService"/> liveness via the shared
/// <see cref="OutboxArchivalState"/>. Returns <see cref="HealthStatus.Healthy"/> when the
/// last successful batch is within <see cref="OutboxArchivalHealthCheckOptions.DegradedAfter"/>,
/// <see cref="HealthStatus.Degraded"/> when between <c>DegradedAfter</c> and
/// <c>UnhealthyAfter</c>, and <see cref="HealthStatus.Unhealthy"/> when older than
/// <c>UnhealthyAfter</c>. A service that has never produced a batch yet is reported as
/// <see cref="HealthStatus.Degraded"/> with the start-time context so operators can tell
/// "warming up" apart from "stuck".
/// </summary>
public sealed class OutboxArchivalHealthCheck : IHealthCheck
{
    private readonly OutboxArchivalState state;
    private readonly OutboxArchivalHealthCheckOptions options;
    private readonly TimeProvider clock;

    public OutboxArchivalHealthCheck(OutboxArchivalState state, OutboxArchivalHealthCheckOptions options)
        : this(state, options, TimeProvider.System)
    {
    }

    internal OutboxArchivalHealthCheck(
        OutboxArchivalState state,
        OutboxArchivalHealthCheckOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(options);
        this.state = state;
        this.options = options;
        this.options.Validate();
        this.clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var last = state.LastSuccessfulBatchUtc;
        var data = new Dictionary<string, object>
        {
            ["totalBatches"] = state.TotalBatches,
            ["lastSuccessfulBatchUtc"] = (object?)last ?? "never",
            ["nowUtc"] = now,
        };

        if (last is null)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "OrionGuard outbox archival has not completed a batch yet.", data: data));
        }
        var age = now - last.Value;
        if (age >= options.UnhealthyAfter)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Last archival batch was {age.TotalMinutes:F1} minutes ago (>= {options.UnhealthyAfter.TotalMinutes:F0}).",
                data: data));
        }
        if (age >= options.DegradedAfter)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Last archival batch was {age.TotalMinutes:F1} minutes ago (>= {options.DegradedAfter.TotalMinutes:F0}).",
                data: data));
        }
        return Task.FromResult(HealthCheckResult.Healthy(
            $"Last archival batch was {age.TotalSeconds:F0}s ago.", data: data));
    }
}

/// <summary>Options for <see cref="OutboxArchivalHealthCheck"/>.</summary>
public sealed class OutboxArchivalHealthCheckOptions
{
    /// <summary>Threshold past which the health check downgrades to Degraded. Default 5 min.</summary>
    public TimeSpan DegradedAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Threshold past which the health check returns Unhealthy. Default 15 min.</summary>
    public TimeSpan UnhealthyAfter { get; set; } = TimeSpan.FromMinutes(15);

    internal void Validate()
    {
        if (DegradedAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DegradedAfter), DegradedAfter,
                "OutboxArchivalHealthCheckOptions.DegradedAfter must be positive.");
        }
        if (UnhealthyAfter <= DegradedAfter)
        {
            throw new ArgumentOutOfRangeException(
                nameof(UnhealthyAfter), UnhealthyAfter,
                $"OutboxArchivalHealthCheckOptions.UnhealthyAfter ({UnhealthyAfter}) must be greater than DegradedAfter ({DegradedAfter}).");
        }
    }
}
