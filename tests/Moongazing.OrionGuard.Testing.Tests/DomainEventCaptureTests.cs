using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.Testing.Tests;

public class DomainEventCaptureTests
{
    private sealed record OrderShipped(Guid OrderId) : DomainEventBase;
    private sealed record OrderCancelled(Guid OrderId) : DomainEventBase;

    private sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id) : base(id) { }
        public void Ship() => RaiseEvent(new OrderShipped(Id));
        public void Cancel() => RaiseEvent(new OrderCancelled(Id));
    }

    [Fact]
    public void From_PullsEventsFromAggregate()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        order.Cancel();

        var capture = DomainEventCapture.From(order);

        Assert.Equal(2, capture.All.Count);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void Should_HaveRaised_DoesNotThrow_WhenEventPresent()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        var capture = DomainEventCapture.From(order);

        capture.Should().HaveRaised<OrderShipped>(e => e.OrderId == order.Id);
    }

    [Fact]
    public void Should_HaveRaised_Throws_WhenEventNotPresent()
    {
        var capture = DomainEventCapture.From(new Order(Guid.NewGuid()));

        Assert.Throws<DomainEventAssertionException>(() => capture.Should().HaveRaised<OrderShipped>());
    }

    [Fact]
    public void Should_NotHaveRaised_DoesNotThrow_WhenEventAbsent()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        var capture = DomainEventCapture.From(order);

        capture.Should().NotHaveRaised<OrderCancelled>();
    }

    [Fact]
    public void Should_NotHaveRaised_Throws_WhenEventPresent()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        var capture = DomainEventCapture.From(order);

        Assert.Throws<DomainEventAssertionException>(() => capture.Should().NotHaveRaised<OrderShipped>());
    }

    [Fact]
    public void Should_HaveRaisedExactlyOf_VerifiesCount()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        order.Cancel();
        var capture = DomainEventCapture.From(order);

        capture.Should().HaveRaisedExactly(1).Of<OrderShipped>();

        Assert.Throws<DomainEventAssertionException>(() => capture.Should().HaveRaisedExactly(2).Of<OrderShipped>());
    }

    [Fact]
    public void Single_ReturnsTheOnlyEventOfType()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        var capture = DomainEventCapture.From(order);

        var shipped = capture.Single<OrderShipped>();

        Assert.Equal(order.Id, shipped.OrderId);
    }
}
