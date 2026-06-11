namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
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
