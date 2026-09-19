using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

/// <summary>Inline mode inside an explicit <c>Database.BeginTransaction()</c> dispatches only once that transaction commits.</summary>
public class DomainEventInterceptorTransactionTests
{
    [Fact]
    public async Task InlineMode_ExplicitTransactionRolledBack_DispatchesNothing()
    {
        var dispatcher = new RecordingDispatcher();
        await using var serviceProvider = OutboxTestServices.Build(_ => dispatcher, outbox: false);
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var order = new Order(Guid.NewGuid());
            db.Orders.Add(order);
            order.Ship();
            await db.SaveChangesAsync();
            Assert.Empty(dispatcher.Dispatched);   // nothing is committed yet

            await transaction.RollbackAsync();
        }

        Assert.Empty(dispatcher.Dispatched);
        Assert.Equal(0, await OutboxTestServices.OrderCountAsync(serviceProvider));
    }

    [Fact]
    public async Task InlineMode_ExplicitTransaction_DispatchesWhenItCommits()
    {
        var dispatcher = new RecordingDispatcher();
        await using var serviceProvider = OutboxTestServices.Build(_ => dispatcher, outbox: false);
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        await using var transaction = await db.Database.BeginTransactionAsync();
        var order = new Order(Guid.NewGuid());
        db.Orders.Add(order);
        order.Ship();
        await db.SaveChangesAsync();
        order.Cancel();
        await db.SaveChangesAsync();
        Assert.Empty(dispatcher.Dispatched);

        await transaction.CommitAsync();

        Assert.Collection(dispatcher.Dispatched,
            e => Assert.IsType<OrderShipped>(e),
            e => Assert.IsType<OrderCancelled>(e));
    }

    [Fact]
    public async Task InlineMode_ExplicitTransactionCommittedSynchronously_Dispatches()
    {
        var dispatcher = new RecordingDispatcher();
        await using var serviceProvider = OutboxTestServices.Build(_ => dispatcher, outbox: false);
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        using var transaction = db.Database.BeginTransaction();
        var order = new Order(Guid.NewGuid());
        db.Orders.Add(order);
        order.Ship();
        await db.SaveChangesAsync();

        transaction.Commit();

        Assert.IsType<OrderShipped>(Assert.Single(dispatcher.Dispatched));
    }

    [Fact]
    public async Task InlineMode_TransactionDisposedWithoutCommit_ItsEventsAreNeverDispatched()
    {
        var dispatcher = new RecordingDispatcher();
        await using var serviceProvider = OutboxTestServices.Build(_ => dispatcher, outbox: false);
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        await using (await db.Database.BeginTransactionAsync())
        {
            var rolledBack = new Order(Guid.NewGuid());
            db.Orders.Add(rolledBack);
            rolledBack.Ship();
            await db.SaveChangesAsync();
        }   // disposed: rolled back without a Rollback call

        db.ChangeTracker.Clear();
        var committed = new Order(Guid.NewGuid());
        db.Orders.Add(committed);
        committed.Cancel();
        await db.SaveChangesAsync();

        Assert.IsType<OrderCancelled>(Assert.Single(dispatcher.Dispatched));
    }
}
