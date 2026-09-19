using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

/// <summary>
/// Scoped buffer of domain events that did not come from a tracked aggregate. In Inline mode the
/// <see cref="DomainEventSaveChangesInterceptor"/> dispatches whatever was added here via <see cref="Add"/> /
/// <see cref="AddRange"/> (or <see cref="TrackAggregate"/>) after the next successful save in the same DI
/// scope, ahead of the aggregates' own events, and clears it when a save fails. Aggregates tracked by the
/// DbContext are followed per DbContext instance by the interceptor itself, not through this buffer.
/// The collector is not consulted when the DbContext is pooled or built by a factory, because the
/// interceptor then has no request scope to resolve it from. Outbox mode does not use this buffer.
/// </summary>
public sealed class DomainEventCollector
{
    private readonly List<IDomainEvent> events = new();
    private readonly List<IAggregateRoot> pendingAggregates = new();

    /// <summary>Currently buffered events (does not include events still on tracked aggregates).</summary>
    public IReadOnlyList<IDomainEvent> Pending => events;

    /// <summary>
    /// Registers a live aggregate so its events can be pulled later by <see cref="DrainSnapshot"/>.
    /// The aggregate is not drained here; the events remain on the aggregate so they are preserved
    /// across EF execution-strategy retries that re-invoke SavingChanges.
    /// </summary>
    /// <param name="aggregate">The aggregate to track. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="aggregate"/> is null.</exception>
    public void TrackAggregate(IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        pendingAggregates.Add(aggregate);
    }

    /// <summary>Adds an event to the buffer.</summary>
    /// <param name="event">The event to add. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="event"/> is null.</exception>
    public void Add(IDomainEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        events.Add(@event);
    }

    /// <summary>Adds a sequence of events to the buffer.</summary>
    /// <param name="events">The events to add. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="events"/> is null.</exception>
    public void AddRange(IEnumerable<IDomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        this.events.AddRange(events);
    }

    /// <summary>
    /// Pulls events from the still-live tracked aggregates into the event buffer, returns a snapshot
    /// of all pending events, and clears both internal lists.
    /// </summary>
    /// <returns>The events that were buffered (and pulled from tracked aggregates) at the moment of the call.</returns>
    public IReadOnlyList<IDomainEvent> DrainSnapshot()
    {
        foreach (var aggregate in pendingAggregates)
        {
            var pulled = aggregate.PullDomainEvents();
            if (pulled.Count > 0)
            {
                events.AddRange(pulled);
            }
        }
        pendingAggregates.Clear();

        if (events.Count == 0)
        {
            return Array.Empty<IDomainEvent>();
        }
        var snapshot = events.ToArray();
        events.Clear();
        return snapshot;
    }

    /// <summary>
    /// Discards any tracked aggregates and pending events without dispatching. Called when
    /// SaveChanges fails so the next save attempt starts clean. Tracked aggregates are not drained
    /// here — their domain events remain on the aggregate for the next save attempt to observe.
    /// </summary>
    public void Reset()
    {
        events.Clear();
        pendingAggregates.Clear();
    }
}
