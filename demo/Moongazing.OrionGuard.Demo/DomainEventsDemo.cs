using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

namespace Moongazing.OrionGuard.Demo;

public static class DomainEventsDemo
{
    public sealed record OrderShipped(Guid OrderId) : DomainEventBase;

    public sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id) : base(id) { }
        private Order() { }
        public string Status { get; private set; } = "New";
        public void Ship() { Status = "Shipped"; RaiseEvent(new OrderShipped(Id)); }
    }

    public sealed class DemoDbContext : DbContext
    {
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DemoDbContext(DbContextOptions<DemoDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Order>().HasKey(o => o.Id);
            b.Entity<Order>().Ignore(o => o.DomainEvents);
            b.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration("OrionGuard_Outbox"));
        }
    }

    public sealed class LoggingHandler : IDomainEventHandler<OrderShipped>
    {
        public Task HandleAsync(OrderShipped @event, CancellationToken cancellationToken)
        {
            Console.WriteLine($"  -> handled OrderShipped({@event.OrderId})");
            return Task.CompletedTask;
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("\n== Domain Events demo (Inline mode) ==");

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOrionGuardDomainEvents();
        builder.Services.AddOrionGuardDomainEventHandlers(typeof(DomainEventsDemo).Assembly);
        builder.Services.AddOrionGuardEfCore<DemoDbContext>(o => o.UseInline());
        builder.Services.AddDbContext<DemoDbContext>((sp, o) =>
            o.UseSqlite("DataSource=demo.db")
             .UseOrionGuardDomainEvents(sp));

        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DemoDbContext>();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();

        var order = new Order(Guid.NewGuid());
        ctx.Orders.Add(order);
        order.Ship();
        await ctx.SaveChangesAsync();

        Console.WriteLine($"  Saved order {order.Id} (status={order.Status})");
    }
}
