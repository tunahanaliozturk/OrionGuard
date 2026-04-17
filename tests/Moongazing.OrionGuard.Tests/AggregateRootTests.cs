using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class AggregateRootTests
{
    private sealed class OrderPlaced : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
    }

    private sealed class Order : AggregateRoot<int>
    {
        public Order(int id) : base(id) { }

        public void Place() => RaiseEvent(new OrderPlaced());
        public void RaiseNull() => RaiseEvent(null!);
    }

    [Fact]
    public void RaiseEvent_ShouldAppendToDomainEvents_WhenCalled()
    {
        var order = new Order(1);
        order.Place();
        order.Place();

        Assert.Equal(2, order.DomainEvents.Count);
    }

    [Fact]
    public void PullDomainEvents_ShouldReturnAndClear_WhenCalled()
    {
        var order = new Order(1);
        order.Place();
        order.Place();

        var pulled = order.PullDomainEvents();

        Assert.Equal(2, pulled.Count);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void PullDomainEvents_ShouldReturnEmpty_WhenNoEventsRaised()
    {
        var order = new Order(1);

        var pulled = order.PullDomainEvents();

        Assert.Empty(pulled);
    }

    [Fact]
    public void RaiseEvent_ShouldThrow_WhenEventIsNull()
    {
        var order = new Order(1);

        Assert.Throws<ArgumentNullException>(order.RaiseNull);
    }

    [Fact]
    public void IAggregateRoot_ShouldBeAssignableFromAggregateRootOfT()
    {
        var order = new Order(1);

        Assert.IsAssignableFrom<IAggregateRoot>(order);
    }
}
