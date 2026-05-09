using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.Testing.Tests;

public class InMemoryDomainEventDispatcherTests
{
    private sealed record OrderShipped(Guid OrderId) : DomainEventBase;

    [Fact]
    public async Task DispatchAsync_StoresEvents_InCapturedList()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        var evt = new OrderShipped(Guid.NewGuid());

        await dispatcher.DispatchAsync(evt);

        Assert.Single(dispatcher.Captured);
        Assert.Same(evt, dispatcher.Captured[0]);
    }

    [Fact]
    public async Task DispatchAsync_BatchOverload_StoresAllEvents()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        var batch = new IDomainEvent[] { new OrderShipped(Guid.NewGuid()), new OrderShipped(Guid.NewGuid()) };

        await dispatcher.DispatchAsync(batch);

        Assert.Equal(2, dispatcher.Captured.Count);
    }

    [Fact]
    public async Task Should_ProvidesAssertionsAcrossCapturedEvents()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await dispatcher.DispatchAsync(new OrderShipped(Guid.Parse("00000000-0000-0000-0000-000000000001")));

        dispatcher.Should().HaveRaised<OrderShipped>(e => e.OrderId == Guid.Parse("00000000-0000-0000-0000-000000000001"));
    }

    [Fact]
    public async Task Clear_RemovesAllCapturedEvents()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await dispatcher.DispatchAsync(new OrderShipped(Guid.NewGuid()));

        dispatcher.Clear();

        Assert.Empty(dispatcher.Captured);
    }
}
