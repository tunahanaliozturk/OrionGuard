using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

public class DomainEventInterceptorOutboxTests
{
    private static ServiceProvider BuildSp(InMemoryDomainEventDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);

        services.AddSingleton(new OrionGuardEfCoreOptions().UseOutbox());
        services.AddScoped<DomainEventCollector>();
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite("DataSource=:memory:")
             .AddInterceptors(new DomainEventSaveChangesInterceptor(sp)));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SaveChanges_OutboxMode_WritesOutboxRowsInsteadOfDispatching()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        var order = new Order(Guid.NewGuid());
        ctx.Orders.Add(order);
        order.Ship();
        await ctx.SaveChangesAsync();

        // No inline dispatch in Outbox mode:
        Assert.Empty(dispatcher.Captured);

        // One outbox row written transactionally:
        var rows = await ctx.OutboxMessages.AsNoTracking().ToListAsync();
        var row = Assert.Single(rows);
        Assert.Contains(nameof(OrderShipped), row.EventType);
        Assert.Null(row.ProcessedOnUtc);
        Assert.Equal(0, row.RetryCount);
    }

    [Fact]
    public async Task SaveChanges_OutboxMode_NoAggregates_WritesNoOutboxRows()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        ctx.Orders.Add(new Order(Guid.NewGuid()));   // no Ship() call
        await ctx.SaveChangesAsync();

        Assert.Empty(await ctx.OutboxMessages.AsNoTracking().ToListAsync());
    }
}
