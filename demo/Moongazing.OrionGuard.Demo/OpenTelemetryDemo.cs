using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.OpenTelemetry.DomainEvents;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Demonstrates OpenTelemetry instrumentation on the dispatcher. A minimal
/// <see cref="ActivityListener"/> is attached directly (rather than wiring the full OpenTelemetry SDK)
/// so the demo does not pull in the SDK as a dependency. The principle is the same; a real
/// app calls <c>AddOpenTelemetry().WithTracing(t =&gt; t.AddSource(...))</c>.
/// </summary>
public static class OpenTelemetryDemo
{
    public sealed record CartCheckedOut(Guid CartId, decimal Total) : DomainEventBase;

    public sealed class CartHandler : IDomainEventHandler<CartCheckedOut>
    {
        public Task HandleAsync(CartCheckedOut @event, CancellationToken cancellationToken)
        {
            return Task.Delay(5, cancellationToken);
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("\n== OpenTelemetry instrumentation demo ==");

        var capturedSpans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OrionGuardDomainEventTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => capturedSpans.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        services.AddOrionGuardDomainEventHandlers(typeof(OpenTelemetryDemo).Assembly);
        services.WithOpenTelemetryDomainEvents();

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new CartCheckedOut(Guid.NewGuid(), 49.99m));
        await dispatcher.DispatchAsync(new CartCheckedOut(Guid.NewGuid(), 12.50m));

        Console.WriteLine($"  spans recorded: {capturedSpans.Count}");
        foreach (var span in capturedSpans)
        {
            var typeTag = span.GetTagItem("orionguard.event.type") as string;
            Console.WriteLine($"  - {span.DisplayName} status={span.Status} duration={span.Duration.TotalMilliseconds:0.00}ms event_type={typeTag}");
        }
    }
}
