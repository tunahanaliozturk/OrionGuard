using System;

namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>
/// Base record for domain events. Assigns a fresh <see cref="EventId"/> and a UTC
/// <see cref="OccurredOnUtc"/> timestamp at construction time; both members use
/// <see langword="init"/> accessors so tests can pin them via <c>with</c> expressions.
/// </summary>
/// <remarks>
/// Consumers write <c>public sealed record OrderPlaced(OrderId Id) : DomainEventBase;</c>
/// and the canonical <see cref="IDomainEvent"/> members are populated automatically.
/// The event dispatcher (<c>IDomainEventDispatcher</c>, MediatR bridge, EF Core
/// interceptor) arrives in v6.3.0 and operates on the <see cref="IDomainEvent"/>
/// abstraction — this record is orthogonal to dispatch wiring.
/// </remarks>
public abstract record DomainEventBase : IDomainEvent
{
    /// <summary>Globally unique identifier for this event instance.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>Timestamp in UTC at which the event was raised.</summary>
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
