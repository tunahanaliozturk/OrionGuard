using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

public class DomainEventInterceptorInlineTests
{
    private static ServiceProvider BuildSp(InMemoryDomainEventDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);

        services.AddSingleton(new OrionGuardEfCoreOptions().UseInline());
        services.AddScoped<DomainEventCollector>();
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite("DataSource=:memory:")
             .AddInterceptors(new DomainEventSaveChangesInterceptor(sp)));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SaveChanges_DispatchesEventsRaisedByAggregates()
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

        Assert.Single(dispatcher.Captured);
        Assert.IsType<OrderShipped>(dispatcher.Captured[0]);
    }

    [Fact]
    public async Task SaveChanges_EmptiesAggregateBufferAfterDispatch()
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

        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public async Task SaveChanges_NoEvents_DoesNothing()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        ctx.Orders.Add(new Order(Guid.NewGuid()));
        await ctx.SaveChangesAsync();

        Assert.Empty(dispatcher.Captured);
    }
}
