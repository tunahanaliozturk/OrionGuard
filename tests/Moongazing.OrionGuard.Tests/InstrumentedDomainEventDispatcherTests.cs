using System.Diagnostics;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.OpenTelemetry.DomainEvents;

namespace Moongazing.OrionGuard.Tests;

public class InstrumentedDomainEventDispatcherTests
{
    private sealed record TestEvent(int Id) : DomainEventBase;

    private sealed class StubDispatcher : IDomainEventDispatcher
    {
        public bool Throw { get; set; }
        public Task DispatchAsync(IDomainEvent @event, CancellationToken ct = default)
            => Throw ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task DispatchAsync_OnSuccess_StartsAndCompletesActivity()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OrionGuardDomainEventTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        var inner = new StubDispatcher();
        var instr = new InstrumentedDomainEventDispatcher(inner);

        await instr.DispatchAsync(new TestEvent(1));

        var activity = Assert.Single(captured);
        Assert.Equal("DomainEvent.Dispatch TestEvent", activity.DisplayName);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    [Fact]
    public async Task DispatchAsync_OnFailure_RecordsErrorAndRethrows()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OrionGuardDomainEventTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        var inner = new StubDispatcher { Throw = true };
        var instr = new InstrumentedDomainEventDispatcher(inner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => instr.DispatchAsync(new TestEvent(1)));
        var activity = Assert.Single(captured);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }
}
