using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
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

    [Fact]
    public void WithOpenTelemetryDomainEvents_DecoratesFactoryBasedRegistration()
    {
        var inner = new StubDispatcher();

        var services = new ServiceCollection();
        services.AddScoped<IDomainEventDispatcher>(_ => inner);   // factory, not type
        services.WithOpenTelemetryDomainEvents();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        Assert.IsType<InstrumentedDomainEventDispatcher>(dispatcher);
    }

    [Fact]
    public void WithOpenTelemetryDomainEvents_CalledTwice_DoesNotDoubleDecorate()
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        services.WithOpenTelemetryDomainEvents();
        services.WithOpenTelemetryDomainEvents();   // second call must be a no-op

        var dispatcherCount = services.Count(d => d.ServiceType == typeof(IDomainEventDispatcher));
        var markerCount = services.Count(d => d.ServiceType == typeof(WithOpenTelemetryDomainEventsMarker));

        Assert.Equal(1, dispatcherCount);
        Assert.Equal(1, markerCount);
    }

    [Fact]
    public void WithOpenTelemetryDomainEvents_WithoutPriorRegistration_Throws()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() => services.WithOpenTelemetryDomainEvents());
        Assert.Contains("No IDomainEventDispatcher", ex.Message);
    }
}
