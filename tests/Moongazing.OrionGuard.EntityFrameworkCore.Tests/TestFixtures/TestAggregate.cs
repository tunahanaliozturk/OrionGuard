using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

public sealed record OrderShipped(Guid OrderId) : DomainEventBase;
public sealed record OrderCancelled(Guid OrderId) : DomainEventBase;

public sealed class Order : AggregateRoot<Guid>
{
    public string Status { get; private set; } = "New";
    public Order(Guid id) : base(id) { }
    private Order() { }   // EF
    public void Ship() { Status = "Shipped"; RaiseEvent(new OrderShipped(Id)); }
    public void Cancel() { Status = "Cancelled"; RaiseEvent(new OrderCancelled(Id)); }
}
