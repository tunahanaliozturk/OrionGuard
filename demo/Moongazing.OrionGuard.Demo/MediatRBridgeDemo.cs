using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.MediatR.DomainEvents;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Demonstrates the MediatR bridge: events implement <see cref="INotification"/> and are
/// published through MediatR's <see cref="IPublisher"/>, so MediatR pipeline behaviours compose
/// naturally.
/// </summary>
public static class MediatRBridgeDemo
{
    public sealed record OrderPlaced(Guid OrderId, decimal Total) : DomainEventBase, INotification;

    public sealed class EmailNotificationHandler : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            Console.WriteLine($"  email handler: queued confirmation for order {notification.OrderId}");
            return Task.CompletedTask;
        }
    }

    public sealed class AnalyticsHandler : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            Console.WriteLine($"  analytics handler: tracked order_placed amount={notification.Total:0.00}");
            return Task.CompletedTask;
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("\n== MediatR bridge demo ==");

        var services = new ServiceCollection();
        services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<EmailNotificationHandler>());
        services.AddOrionGuardDomainEvents();
        services.AddOrionGuardMediatRDomainEvents();

        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        var evt = new OrderPlaced(Guid.NewGuid(), 199.90m);
        await dispatcher.DispatchAsync(evt);

        Console.WriteLine($"  dispatcher in use: {dispatcher.GetType().Name}");
    }
}
