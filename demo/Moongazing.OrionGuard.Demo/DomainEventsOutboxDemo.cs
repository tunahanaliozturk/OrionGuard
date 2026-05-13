using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Demonstrates Outbox mode: events are persisted as <c>OutboxMessage</c> rows in the same
/// transaction as the aggregate state, then dispatched asynchronously by
/// <see cref="OutboxDispatcherHostedService"/>.
/// </summary>
public static class DomainEventsOutboxDemo
{
    public sealed class OutboxDbContext : DbContext
    {
        public DbSet<DomainEventsDemo.Order> Orders => Set<DomainEventsDemo.Order>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

        public OutboxDbContext(DbContextOptions<OutboxDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<DomainEventsDemo.Order>().HasKey(o => o.Id);
            b.Entity<DomainEventsDemo.Order>().Ignore(o => o.DomainEvents);
            b.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration("OrionGuard_Outbox"));
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("\n== Domain Events demo (Outbox mode) ==");

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOrionGuardDomainEvents();
        builder.Services.AddOrionGuardDomainEventHandlers(typeof(DomainEventsOutboxDemo).Assembly);
        builder.Services.AddOrionGuardEfCore<OutboxDbContext>(o => o.UseOutbox(opt =>
        {
            opt.PollingInterval = TimeSpan.FromMilliseconds(200);
            opt.BatchSize = 10;
        }));
        builder.Services.AddDbContext<OutboxDbContext>((sp, o) =>
            o.UseSqlite("DataSource=outbox-demo.db")
             .UseOrionGuardDomainEvents(sp));

        using var host = builder.Build();

        await using (var seedScope = host.Services.CreateAsyncScope())
        {
            var ctx = seedScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            await ctx.Database.EnsureDeletedAsync();
            await ctx.Database.EnsureCreatedAsync();

            var order = new DomainEventsDemo.Order(Guid.NewGuid());
            ctx.Orders.Add(order);
            order.Ship();
            await ctx.SaveChangesAsync();

            var pending = await ctx.OutboxMessages.AsNoTracking().CountAsync();
            Console.WriteLine($"  Order saved. Outbox rows pending dispatch: {pending}");
        }

        await host.StartAsync();
        await WaitForDrainAsync(host.Services, TimeSpan.FromSeconds(5));
        await host.StopAsync();

        await using (var checkScope = host.Services.CreateAsyncScope())
        {
            var ctx = checkScope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var processed = await ctx.OutboxMessages.AsNoTracking()
                .CountAsync(m => m.ProcessedOnUtc != null);
            var unprocessed = await ctx.OutboxMessages.AsNoTracking()
                .CountAsync(m => m.ProcessedOnUtc == null);
            Console.WriteLine($"  Worker drained queue: processed={processed}, unprocessed={unprocessed}");
        }
    }

    private static async Task WaitForDrainAsync(IServiceProvider rootServices, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            await using var scope = rootServices.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var unprocessed = await ctx.OutboxMessages.AsNoTracking()
                .CountAsync(m => m.ProcessedOnUtc == null);
            if (unprocessed == 0) return;
        }
    }
}
