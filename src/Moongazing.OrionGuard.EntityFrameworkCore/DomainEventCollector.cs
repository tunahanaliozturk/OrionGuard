using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

/// <summary>
/// Per-DbContext-scope buffer that holds events pulled by the interceptor at SavingChanges so they
/// can be dispatched at SavedChanges (Inline mode). Outbox mode does not use this buffer; it writes
/// rows to the DbContext directly.
/// </summary>
public sealed class DomainEventCollector
{
    private readonly List<IDomainEvent> events = new();

    /// <summary>Currently buffered events.</summary>
    public IReadOnlyList<IDomainEvent> Pending => events;

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

    /// <summary>Returns a snapshot and clears the buffer atomically.</summary>
    /// <returns>The events that were buffered at the moment of the call.</returns>
    public IReadOnlyList<IDomainEvent> DrainSnapshot()
    {
        var snapshot = events.ToArray();
        events.Clear();
        return snapshot;
    }
}
