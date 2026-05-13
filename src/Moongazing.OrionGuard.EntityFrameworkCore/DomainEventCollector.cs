using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

/// <summary>
/// Per-DbContext-scope buffer used by the interceptor to bridge SavingChanges and SavedChanges in
/// Inline mode. Holds two kinds of state:
/// <list type="bullet">
/// <item><description>Live <see cref="IAggregateRoot"/> references registered at SavingChanges (events
/// remain on the aggregate so an <see cref="Microsoft.EntityFrameworkCore.Storage.IExecutionStrategy"/>
/// retry does not lose them).</description></item>
/// <item><description>Pre-pulled <see cref="IDomainEvent"/> instances added directly via
/// <see cref="Add"/> / <see cref="AddRange"/>.</description></item>
/// </list>
/// Outbox mode does not use this buffer; it writes rows to the DbContext directly.
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
