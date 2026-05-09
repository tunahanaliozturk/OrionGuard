using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Testing.DomainEvents;

/// <summary>
/// Captures a snapshot of domain events for assertion. Use <see cref="From(IAggregateRoot)"/> in
/// unit tests; use <see cref="FromList(IEnumerable{IDomainEvent})"/> with
/// <c>InMemoryDomainEventDispatcher</c> in integration tests.
/// </summary>
public sealed class DomainEventCapture
{
    private readonly List<IDomainEvent> events;

    private DomainEventCapture(IEnumerable<IDomainEvent> events) => this.events = events.ToList();

    /// <summary>All captured events in raise order.</summary>
    public IReadOnlyList<IDomainEvent> All => events;

    /// <summary>Pulls events out of an aggregate's buffer (empties the buffer).</summary>
    public static DomainEventCapture From(IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        return new DomainEventCapture(aggregate.PullDomainEvents());
    }

    /// <summary>Wraps an existing event list (does not pull from anywhere).</summary>
    public static DomainEventCapture FromList(IEnumerable<IDomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return new DomainEventCapture(events);
    }

    /// <summary>Returns the single event of type <typeparamref name="TEvent"/>; throws if zero or more than one.</summary>
    public TEvent Single<TEvent>() where TEvent : IDomainEvent
        => events.OfType<TEvent>().Single();

    /// <summary>All captured events of type <typeparamref name="TEvent"/>.</summary>
    public IEnumerable<TEvent> OfType<TEvent>() where TEvent : IDomainEvent
        => events.OfType<TEvent>();

    /// <summary>True when at least one event of type <typeparamref name="TEvent"/> was captured.</summary>
    public bool Contains<TEvent>() where TEvent : IDomainEvent
        => events.OfType<TEvent>().Any();

    /// <summary>True when at least one event of type <typeparamref name="TEvent"/> matching <paramref name="predicate"/> was captured.</summary>
    public bool Contains<TEvent>(Func<TEvent, bool> predicate) where TEvent : IDomainEvent
        => events.OfType<TEvent>().Any(predicate);

    /// <summary>Entry point for fluent assertions.</summary>
    public DomainEventAssertions Should() => new(this);
}
