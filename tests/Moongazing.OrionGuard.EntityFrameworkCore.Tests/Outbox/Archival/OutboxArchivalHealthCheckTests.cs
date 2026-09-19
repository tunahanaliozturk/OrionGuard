namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Xunit;

public sealed class OutboxArchivalHealthCheckTests
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static OutboxArchivalHealthCheck NewSut(
        OutboxArchivalState state, DateTime nowUtc,
        TimeSpan? degraded = null, TimeSpan? unhealthy = null)
    {
        var clock = new FixedClock { Now = new DateTimeOffset(nowUtc, TimeSpan.Zero) };
        return new OutboxArchivalHealthCheck(
            state,
            new OutboxArchivalHealthCheckOptions
            {
                DegradedAfter = degraded ?? TimeSpan.FromMinutes(5),
                UnhealthyAfter = unhealthy ?? TimeSpan.FromMinutes(15),
            },
            clock);
    }

    [Fact]
    public async Task Reports_Degraded_when_no_batch_has_completed_yet()
    {
        var sut = NewSut(new OutboxArchivalState(), nowUtc: DateTime.UtcNow);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal("never", result.Data["lastSuccessfulBatchUtc"]);
    }

    [Fact]
    public async Task Reports_Healthy_when_last_batch_is_within_DegradedAfter()
    {
        var state = new OutboxArchivalState();
        var now = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        state.RecordSuccessfulBatch(now.AddSeconds(-30));
        var sut = NewSut(state, nowUtc: now);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Reports_Degraded_when_last_batch_is_between_thresholds()
    {
        var state = new OutboxArchivalState();
        var now = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        state.RecordSuccessfulBatch(now.AddMinutes(-8));
        var sut = NewSut(state, nowUtc: now);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task Reports_Unhealthy_when_last_batch_is_older_than_UnhealthyAfter()
    {
        var state = new OutboxArchivalState();
        var now = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        state.RecordSuccessfulBatch(now.AddMinutes(-20));
        var sut = NewSut(state, nowUtc: now);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // The archival worker reports its polling interval to the shared state when it is constructed.
    private static OutboxArchivalState StateOfWorkerPollingEvery(TimeSpan pollingInterval)
    {
        var state = new OutboxArchivalState();
        _ = new OutboxArchivalHostedService(
            new OutboxArchivalOptions { PollingInterval = pollingInterval },
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new NullDistributedLock(),
            logger: null,
            archiver: null,
            state);
        return state;
    }

    private static OutboxArchivalHealthCheck NewSutWithDefaultThresholds(OutboxArchivalState state, DateTime nowUtc)
        => new(state, new OutboxArchivalHealthCheckOptions(), new FixedClock { Now = new DateTimeOffset(nowUtc, TimeSpan.Zero) });

    [Fact]
    public async Task Default_thresholds_report_Healthy_between_hourly_archival_batches()
    {
        // The fixed 15 minute default reported every healthy hourly worker as Unhealthy between batches.
        var now = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        var state = StateOfWorkerPollingEvery(TimeSpan.FromHours(1));
        state.RecordSuccessfulBatch(now.AddMinutes(-50));

        var result = await NewSutWithDefaultThresholds(state, now).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Theory]
    [InlineData(119, HealthStatus.Healthy)]
    [InlineData(121, HealthStatus.Degraded)]     // two missed intervals
    [InlineData(181, HealthStatus.Unhealthy)]    // three missed intervals
    public async Task Default_thresholds_follow_the_archival_polling_interval(int minutesSinceLastBatch, HealthStatus expected)
    {
        var now = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        var state = StateOfWorkerPollingEvery(TimeSpan.FromHours(1));
        state.RecordSuccessfulBatch(now.AddMinutes(-minutesSinceLastBatch));

        var result = await NewSutWithDefaultThresholds(state, now).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Explicit_thresholds_win_over_the_archival_polling_interval()
    {
        var now = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        var state = StateOfWorkerPollingEvery(TimeSpan.FromHours(1));
        state.RecordSuccessfulBatch(now.AddMinutes(-20));

        var result = await NewSut(state, now, degraded: TimeSpan.FromMinutes(5), unhealthy: TimeSpan.FromMinutes(15))
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public void Options_Validate_rejects_UnhealthyAfter_le_DegradedAfter()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OutboxArchivalHealthCheck(
                new OutboxArchivalState(),
                new OutboxArchivalHealthCheckOptions
                {
                    DegradedAfter = TimeSpan.FromMinutes(10),
                    UnhealthyAfter = TimeSpan.FromMinutes(5),
                }));
    }

    [Fact]
    public void State_RecordSuccessfulBatch_increments_total_and_updates_timestamp()
    {
        var state = new OutboxArchivalState();
        var t1 = new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddMinutes(1);

        Assert.Null(state.LastSuccessfulBatchUtc);
        Assert.Equal(0, state.TotalBatches);

        state.RecordSuccessfulBatch(t1);
        Assert.Equal(t1, state.LastSuccessfulBatchUtc);
        Assert.Equal(1, state.TotalBatches);

        state.RecordSuccessfulBatch(t2);
        Assert.Equal(t2, state.LastSuccessfulBatchUtc);
        Assert.Equal(2, state.TotalBatches);
    }
}
