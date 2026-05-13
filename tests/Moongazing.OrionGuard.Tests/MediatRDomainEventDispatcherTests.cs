using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.MediatR.DomainEvents;

namespace Moongazing.OrionGuard.Tests;

public class MediatRDomainEventDispatcherTests
{
    public sealed record OrderShipped(Guid OrderId) : DomainEventBase, INotification;
    public sealed record LegacyEvent(Guid Id) : DomainEventBase;   // no INotification

    public sealed class OrderShippedHandler : INotificationHandler<OrderShipped>
    {
        public List<OrderShipped> Received { get; } = new();
        public Task Handle(OrderShipped notification, CancellationToken ct)
        {
            Received.Add(notification);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DispatchAsync_PublishesEventThroughMediatR_WhenEventImplementsINotification()
    {
        var handler = new OrderShippedHandler();
        var services = new ServiceCollection();
        services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<MediatRDomainEventDispatcherTests>());
        services.AddSingleton<INotificationHandler<OrderShipped>>(handler);
        services.AddOrionGuardMediatRDomainEvents();
        var sp = services.BuildServiceProvider();

        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();
        var evt = new OrderShipped(Guid.NewGuid());
        await dispatcher.DispatchAsync(evt);

        Assert.Single(handler.Received);
        Assert.Equal(evt.OrderId, handler.Received[0].OrderId);
    }

    [Fact]
    public async Task DispatchAsync_Throws_WhenEventDoesNotImplementINotification()
    {
        var services = new ServiceCollection();
        services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<MediatRDomainEventDispatcherTests>());
        services.AddOrionGuardMediatRDomainEvents();
        var sp = services.BuildServiceProvider();

        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new LegacyEvent(Guid.NewGuid())));
    }
}
