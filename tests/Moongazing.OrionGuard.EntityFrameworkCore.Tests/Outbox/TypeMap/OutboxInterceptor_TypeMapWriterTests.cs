using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.TypeMap;

public class OutboxInterceptor_TypeMapWriterTests
{
    private static ServiceProvider BuildSp(OutboxTypeMapRegistry? typeMap)
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton<IDomainEventDispatcher>(new InMemoryDomainEventDispatcher());

        services.AddSingleton(new OrionGuardEfCoreOptions().UseOutbox());
        services.AddScoped<DomainEventCollector>();

        if (typeMap is not null)
        {
            services.AddSingleton(typeMap);
        }

        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite("DataSource=:memory:")
             .AddInterceptors(new DomainEventSaveChangesInterceptor(sp)));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SaveChanges_OutboxMode_WritesLogicalName_WhenTypeMapRegistered()
    {
        var typeMap = new OutboxTypeMapRegistry().Map<OrderShipped>("order.shipped");
        await using var sp = BuildSp(typeMap);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        var order = new Order(Guid.NewGuid());
        ctx.Orders.Add(order);
        order.Ship();
        await ctx.SaveChangesAsync();

        var row = Assert.Single(await ctx.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Equal("order.shipped", row.EventType);
    }

    [Fact]
    public async Task SaveChanges_OutboxMode_FallsBackToAssemblyQualifiedName_WhenNoTypeMapRegistered()
    {
        await using var sp = BuildSp(typeMap: null);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        var order = new Order(Guid.NewGuid());
        ctx.Orders.Add(order);
        order.Ship();
        await ctx.SaveChangesAsync();

        var row = Assert.Single(await ctx.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Equal(typeof(OrderShipped).AssemblyQualifiedName, row.EventType);
    }
}
