using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

/// <summary>
/// What a handler writes through the scoped DbContext is saved together with its row's processed stamp, and
/// never together with a failure.
/// </summary>
public class OutboxDispatcherRowIsolationTests
{
    [Fact]
    public async Task ProcessBatch_HandlerWritesFailToSave_CountsAsAFailedAttemptAndTheNextRowIsDispatched()
    {
        var existingOrderId = Guid.NewGuid();
        var handlerCalls = 0;
        await using var serviceProvider = OutboxTestServices.Build(scope =>
        {
            var db = scope.GetRequiredService<TestDbContext>();
            return new RecordingDispatcher((e, _) =>
            {
                if (e is OrderShipped)
                {
                    // The handler succeeds (sends an e-mail, say) and records something that violates a
                    // constraint only when it is saved.
                    Interlocked.Increment(ref handlerCalls);
                    db.Orders.Add(new Order(existingOrderId));
                }
                return Task.CompletedTask;
            });
        });
        var poison = OutboxTestServices.Row(new OrderShipped(existingOrderId), DateTime.UtcNow.AddSeconds(-2));
        var next = OutboxTestServices.Row(new OrderCancelled(Guid.NewGuid()), DateTime.UtcNow.AddSeconds(-1));
        await OutboxTestServices.SeedAsync(serviceProvider, new Order(existingOrderId), poison, next);
        var worker = OutboxTestServices.Worker(serviceProvider);

        await worker.ProcessBatchAsync(default);

        var rows = await OutboxTestServices.RowsAsync(serviceProvider);
        Assert.Equal(1, rows[0].RetryCount);
        Assert.Null(rows[0].ProcessedOnUtc);
        Assert.Contains("dispatched, but saving", rows[0].Error);
        Assert.NotNull(rows[1].ProcessedOnUtc);     // the failing row no longer blocks the ones behind it
        Assert.Equal(1, handlerCalls);
    }

    [Fact]
    public async Task ProcessBatch_HandlerWritesKeepFailingToSave_TheRowIsDeadLetteredAfterMaxRetries()
    {
        var existingOrderId = Guid.NewGuid();
        var handlerCalls = 0;
        await using var serviceProvider = OutboxTestServices.Build(scope =>
        {
            var db = scope.GetRequiredService<TestDbContext>();
            return new RecordingDispatcher((_, _) =>
            {
                Interlocked.Increment(ref handlerCalls);
                db.Orders.Add(new Order(existingOrderId));
                return Task.CompletedTask;
            });
        });
        await OutboxTestServices.SeedAsync(serviceProvider, new Order(existingOrderId), OutboxTestServices.Row(new OrderShipped(existingOrderId)));
        var worker = OutboxTestServices.Worker(serviceProvider);

        for (var poll = 0; poll < 5; poll++)
        {
            await worker.ProcessBatchAsync(default);
        }

        var row = Assert.Single(await OutboxTestServices.RowsAsync(serviceProvider));
        Assert.Equal(3, row.RetryCount);            // MaxRetries = 3
        Assert.NotNull(row.ProcessedOnUtc);         // dead-lettered instead of blocking the outbox forever
        Assert.NotNull(row.Error);
        Assert.Equal(3, handlerCalls);
    }

    [Fact]
    public async Task ProcessBatch_ThrowingHandler_WritesItLeftTrackedAreNotSaved()
    {
        await using var serviceProvider = OutboxTestServices.Build(scope =>
        {
            var db = scope.GetRequiredService<TestDbContext>();
            return new RecordingDispatcher((_, _) =>
            {
                db.Orders.Add(new Order(Guid.NewGuid()));
                throw new InvalidOperationException("downstream 503");
            });
        });
        await OutboxTestServices.SeedAsync(serviceProvider, OutboxTestServices.Row(new OrderShipped(Guid.NewGuid())));
        var worker = OutboxTestServices.Worker(serviceProvider);

        await worker.ProcessBatchAsync(default);

        var row = Assert.Single(await OutboxTestServices.RowsAsync(serviceProvider));
        Assert.Equal(1, row.RetryCount);
        Assert.Contains("downstream 503", row.Error);
        Assert.Equal(0, await OutboxTestServices.OrderCountAsync(serviceProvider));
    }

    [Fact]
    public async Task ProcessBatch_SucceedingHandler_WritesItLeftTrackedAreSavedWithTheProcessedStamp()
    {
        await using var serviceProvider = OutboxTestServices.Build(scope =>
        {
            var db = scope.GetRequiredService<TestDbContext>();
            return new RecordingDispatcher((_, _) =>
            {
                db.Orders.Add(new Order(Guid.NewGuid()));
                return Task.CompletedTask;
            });
        });
        await OutboxTestServices.SeedAsync(serviceProvider, OutboxTestServices.Row(new OrderShipped(Guid.NewGuid())));
        var worker = OutboxTestServices.Worker(serviceProvider);

        await worker.ProcessBatchAsync(default);

        var row = Assert.Single(await OutboxTestServices.RowsAsync(serviceProvider));
        Assert.NotNull(row.ProcessedOnUtc);
        Assert.Null(row.Error);
        Assert.Equal(1, await OutboxTestServices.OrderCountAsync(serviceProvider));
    }
}
