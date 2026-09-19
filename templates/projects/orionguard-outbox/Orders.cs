using Microsoft.EntityFrameworkCore;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

namespace Company.Outbox;

public sealed record PlaceOrder(string Sku, int Quantity);

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.Sku, nameof(PlaceOrder.Sku), p => p.NotEmpty().Length(3, 32));
        RuleFor(x => x.Quantity, nameof(PlaceOrder.Quantity), p => p.GreaterThan(0));
    }
}

public sealed record OrderPlaced(Guid OrderId, string Sku, int Quantity) : DomainEventBase;

public sealed class Order : AggregateRoot<Guid>
{
    private Order()
    {
    }

    private Order(Guid id, string sku, int quantity) : base(id)
    {
        Sku = sku;
        Quantity = quantity;
    }

    public string Sku { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public static Order Place(string sku, int quantity)
    {
        var order = new Order(Guid.NewGuid(), sku, quantity);
        order.RaiseEvent(new OrderPlaced(order.Id, sku, quantity));

        return order;
    }
}

public sealed class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : IDomainEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced @event, CancellationToken cancellationToken)
    {
        logger.LogInformation("Order {OrderId} placed: {Quantity} x {Sku}", @event.OrderId, @event.Quantity, @event.Sku);

        return Task.CompletedTask;
    }
}

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // DomainEvents is the aggregate's dispatch buffer, not state to persist.
        modelBuilder.Entity<Order>().Ignore(o => o.DomainEvents);
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration("OrionGuard_Outbox"));
        // The dispatcher takes this lock so only one replica polls the outbox at a time.
        modelBuilder.ApplyConfiguration(new OutboxLockEntityTypeConfiguration());
    }
}
