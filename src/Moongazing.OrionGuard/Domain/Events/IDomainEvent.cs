using System;

namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>
/// Represents a domain event raised by an aggregate root. Ships as an interface in v6.1.0;
/// the accompanying <c>DomainEventBase</c> record, <c>IDomainEventDispatcher</c>, and MediatR
/// bridge arrive in v6.2.0.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Globally unique identifier for this event instance.</summary>
    Guid EventId { get; }

    /// <summary>Timestamp in UTC at which the event was raised.</summary>
    DateTime OccurredOnUtc { get; }
}
