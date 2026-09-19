using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

/// <summary>A synchronous <c>SaveChanges()</c> goes through the same event handling as <c>SaveChangesAsync()</c>.</summary>
public class DomainEventInterceptorSyncSaveChangesTests
{
    [Fact]
    public async Task SaveChanges_Sync_OutboxMode_WritesTheOutboxRow()
    {
        var dispatcher = new RecordingDispatcher();
        await using var serviceProvider = OutboxTestServices.Build(_ => dispatcher);
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var order = new Order(Guid.NewGuid());
        db.Orders.Add(order);
        order.Ship();
        db.SaveChanges();

        var row = Assert.Single(await db.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Contains(nameof(OrderShipped), row.EventType);
        Assert.Empty(order.DomainEvents);
        Assert.Empty(dispatcher.Dispatched);
    }

    [Fact]
    public async Task SaveChanges_Sync_InlineMode_DispatchesTheEventsAfterTheSave()
    {
        var dispatcher = new RecordingDispatcher();
        await using var serviceProvider = OutboxTestServices.Build(_ => dispatcher, outbox: false);
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var order = new Order(Guid.NewGuid());
        db.Orders.Add(order);
        order.Ship();
        db.SaveChanges();

        Assert.IsType<OrderShipped>(Assert.Single(dispatcher.Dispatched));
        Assert.Empty(order.DomainEvents);
        Assert.Equal(1, await OutboxTestServices.OrderCountAsync(serviceProvider));
    }

    [Fact]
    public async Task SaveChanges_Sync_InlineMode_HandlerException_SurfacesUnwrapped()
    {
        var dispatcher = new RecordingDispatcher((_, _) => throw new InvalidOperationException("handler failed"));
        await using var serviceProvider = OutboxTestServices.Build(_ => dispatcher, outbox: false);
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var order = new Order(Guid.NewGuid());
        db.Orders.Add(order);
        order.Ship();

        // Same contract as SaveChangesAsync: the data is saved, then the handler's exception leaves SaveChanges.
        var thrown = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal("handler failed", thrown.Message);
        Assert.Equal(1, await OutboxTestServices.OrderCountAsync(serviceProvider));
    }
}
