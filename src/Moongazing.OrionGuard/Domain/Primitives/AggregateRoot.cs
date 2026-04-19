using System;
using System.Collections.Generic;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Base class for DDD aggregate roots — entities that own a cluster of related objects and
/// serve as the single transactional boundary.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>Gets the domain events currently queued on this aggregate.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot{TId}"/> class with the given identifier.
    /// </summary>
    /// <param name="id">The aggregate root identifier. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null.</exception>
    protected AggregateRoot(TId id) : base(id) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot{TId}"/> class without an identifier.
    /// This constructor is provided for Entity Framework Core and serializer scenarios.
    /// </summary>
    protected AggregateRoot() { }

    /// <summary>Queues a domain event to be dispatched after the unit of work completes.</summary>
    /// <param name="event">The domain event to queue. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="event"/> is null.</exception>
    protected void RaiseEvent(IDomainEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _domainEvents.Add(@event);
    }

    /// <summary>
    /// Returns a snapshot of the queued events and clears the internal buffer. Intended to be
    /// called by a dispatcher immediately before publishing.
    /// </summary>
    /// <returns>A read-only collection containing the queued events at the time of the call.</returns>
    public IReadOnlyCollection<IDomainEvent> PullDomainEvents()
    {
        var snapshot = _domainEvents.ToArray();
        _domainEvents.Clear();
        return snapshot;
    }
}
