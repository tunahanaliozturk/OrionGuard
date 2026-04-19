using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Demo.Domain;

// Domain events implement IDomainEvent. The full DomainEventBase record
// ships in OrionGuard v6.2; in v6.1 you implement the interface directly.
public sealed record OrderPlacedEvent(OrderId OrderId, Money Total) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record OrderShippedEvent(OrderId OrderId, DateTime ShippedAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = ShippedAt;
}
