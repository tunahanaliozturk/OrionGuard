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

    [Fact]
    public async Task SaveChanges_AfterFailedAndRetried_StillDispatchesEvents()
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

        var collector = scope.ServiceProvider.GetRequiredService<DomainEventCollector>();

        // After first successful save:
        await ctx.SaveChangesAsync();
        Assert.Single(dispatcher.Captured);          // dispatched
        Assert.Empty(order.DomainEvents);             // aggregate drained at SavedChanges
        Assert.Empty(collector.Pending);              // collector flushed

        // Raise more events on the same aggregate and try again — must work cleanly.
        order.Cancel();
        await ctx.SaveChangesAsync();
        Assert.Equal(2, dispatcher.Captured.Count);
        Assert.Empty(order.DomainEvents);
        Assert.Empty(collector.Pending);
    }

    [Fact]
    public async Task SaveChangesFailed_ResetsCollector_AggregateEventsSurvive()
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

        // Simulate the SavingChangesAsync contract: TrackAggregate registers the live reference
        // but does NOT drain it. Pending stays empty; events remain on the aggregate.
        var collector = scope.ServiceProvider.GetRequiredService<DomainEventCollector>();
        collector.TrackAggregate(order);
        Assert.Empty(collector.Pending);             // TrackAggregate does not buffer events
        Assert.NotEmpty(order.DomainEvents);          // events still on aggregate

        // Reset (the SaveChangesFailedAsync path): aggregate's events must survive the reset
        // because TrackAggregate never pulled them.
        collector.Reset();
        Assert.NotEmpty(order.DomainEvents);          // aggregate still has its events
        Assert.Empty(collector.Pending);

        // After reset, a subsequent DrainSnapshot returns nothing — no stale references.
        var drained = collector.DrainSnapshot();
        Assert.Empty(drained);
        Assert.NotEmpty(order.DomainEvents);          // and the aggregate is still intact
    }

    [Fact]
    public async Task UseOrionGuardDomainEvents_Extension_WiresInterceptorCorrectly()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);

        services.AddOrionGuardEfCore<TestDbContext>(o => o.UseInline());
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite("DataSource=:memory:")
             .UseOrionGuardDomainEvents(sp));   // <-- the new path

        await using var sp = services.BuildServiceProvider();
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
}
