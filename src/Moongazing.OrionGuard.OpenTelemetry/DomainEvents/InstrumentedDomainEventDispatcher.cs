using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.OpenTelemetry.DomainEvents;

/// <summary>
/// Decorates an <see cref="IDomainEventDispatcher"/> with OpenTelemetry spans, metrics counters,
/// and exception status. Use via <c>WithOpenTelemetryDomainEvents()</c> after registering the inner
/// dispatcher.
/// </summary>
public sealed class InstrumentedDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IDomainEventDispatcher inner;

    /// <summary>Initializes a new decorator wrapping <paramref name="inner"/>.</summary>
    /// <param name="inner">The dispatcher to decorate with telemetry.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is <see langword="null"/>.</exception>
    public InstrumentedDomainEventDispatcher(IDomainEventDispatcher inner)
        => this.inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <inheritdoc />
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Canonical DDD term aligned with the IDomainEventDispatcher contract.")]
    [SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Exception is rethrown after recording status; the catch is purely instrumentation.")]
    public async Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var typeName = @event.GetType().Name;
        using var activity = OrionGuardDomainEventTelemetry.ActivitySource
            .StartActivity($"DomainEvent.Dispatch {typeName}", ActivityKind.Internal);
        activity?.SetTag("orionguard.event.id", @event.EventId);
        activity?.SetTag("orionguard.event.type", @event.GetType().FullName);
        activity?.SetTag("orionguard.event.occurred_on", @event.OccurredOnUtc);

        var sw = Stopwatch.StartNew();
        var tags = new TagList { { "event_type", typeName } };
        try
        {
            await inner.DispatchAsync(@event, cancellationToken).ConfigureAwait(false);
            OrionGuardDomainEventTelemetry.EventsDispatched.Add(1, tags);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            OrionGuardDomainEventTelemetry.EventsFailed.Add(1, tags);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message },
            }));
            throw;
        }
        finally
        {
            OrionGuardDomainEventTelemetry.DispatchDuration.Record(sw.Elapsed.TotalMilliseconds, tags);
        }
    }

    /// <inheritdoc />
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
        {
            await DispatchAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }
}
