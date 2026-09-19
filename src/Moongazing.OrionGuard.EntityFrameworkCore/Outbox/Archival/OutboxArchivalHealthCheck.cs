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
/// <remarks>
/// A threshold that is not set explicitly follows the archival worker's
/// <see cref="OutboxArchivalOptions.PollingInterval"/>: Degraded after two intervals and Unhealthy after three,
/// never below the 5 and 15 minute defaults. Without a running worker the defaults apply.
/// </remarks>
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
        var (degradedAfter, unhealthyAfter) = options.Resolve(state.PollingInterval);
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
        if (age >= unhealthyAfter)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Last archival batch was {age.TotalMinutes:F1} minutes ago (>= {unhealthyAfter.TotalMinutes:F0}).",
                data: data));
        }
        if (age >= degradedAfter)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Last archival batch was {age.TotalMinutes:F1} minutes ago (>= {degradedAfter.TotalMinutes:F0}).",
                data: data));
        }
        return Task.FromResult(HealthCheckResult.Healthy(
            $"Last archival batch was {age.TotalSeconds:F0}s ago.", data: data));
    }
}

/// <summary>Options for <see cref="OutboxArchivalHealthCheck"/>.</summary>
public sealed class OutboxArchivalHealthCheckOptions
{
    private static readonly TimeSpan DefaultDegradedAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultUnhealthyAfter = TimeSpan.FromMinutes(15);

    private TimeSpan? degradedAfter;
    private TimeSpan? unhealthyAfter;

    /// <summary>
    /// Threshold past which the health check downgrades to Degraded. When not set: two archival polling intervals
    /// (at least 5 min), or 5 min if no archival worker is running.
    /// </summary>
    public TimeSpan DegradedAfter
    {
        get => degradedAfter ?? DefaultDegradedAfter;
        set => degradedAfter = value;
    }

    /// <summary>
    /// Threshold past which the health check returns Unhealthy. When not set: three archival polling intervals
    /// (at least 15 min), or 15 min if no archival worker is running.
    /// </summary>
    public TimeSpan UnhealthyAfter
    {
        get => unhealthyAfter ?? DefaultUnhealthyAfter;
        set => unhealthyAfter = value;
    }

    // The fixed 5/15 minute defaults are far shorter than the default 1 hour archival interval, so a healthy
    // worker flapped to Unhealthy between batches; thresholds left unset therefore scale with the interval.
    internal (TimeSpan DegradedAfter, TimeSpan UnhealthyAfter) Resolve(TimeSpan? pollingInterval)
    {
        if (pollingInterval is not { } interval)
        {
            return (DegradedAfter, UnhealthyAfter);
        }
        var unhealthy = unhealthyAfter ?? Max(DefaultUnhealthyAfter, interval * 3);
        var degraded = degradedAfter ?? Max(DefaultDegradedAfter, interval * 2);
        if (degraded >= unhealthy)
        {
            // Only reachable with an explicit UnhealthyAfter shorter than two intervals, which Validate() accepted
            // against the fixed 5 minute default: fall back to that default.
            degraded = DefaultDegradedAfter;
        }
        return (degraded, unhealthy);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

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
