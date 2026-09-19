using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

/// <summary>
/// An operator's replay or discard (the dashboard) that lands while the dispatcher is dispatching the same row
/// is not overwritten when the dispatch finishes.
/// </summary>
public class OutboxDispatcherConcurrentChangeTests
{
    [Fact]
    public async Task ProcessBatch_RowReplayedWhileItsDispatchFails_KeepsTheReplay()
    {
        var entered = new SemaphoreSlim(0);
        var release = new SemaphoreSlim(0);
        await using var serviceProvider = OutboxTestServices.Build(_ => new RecordingDispatcher(async (_, _) =>
        {
            entered.Release();
            await release.WaitAsync();
            throw new InvalidOperationException("downstream 503");
        }));
        var row = OutboxTestServices.Row(new OrderShipped(Guid.NewGuid()));
        row.RetryCount = 2;
        row.Error = "earlier failure";
        await OutboxTestServices.SeedAsync(serviceProvider, row);
        var worker = OutboxTestServices.Worker(serviceProvider);

        var batch = worker.ProcessBatchAsync(default);
        await entered.WaitAsync();
        await UpdateAsync(serviceProvider, row.Id, m => { m.RetryCount = 0; m.Error = null; m.ProcessedOnUtc = null; });   // the replay
        release.Release();
        await batch;

        var stored = Assert.Single(await OutboxTestServices.RowsAsync(serviceProvider));
        Assert.Equal(0, stored.RetryCount);         // not the stale 2 + 1, which with MaxRetries = 3 dead-lettered it
        Assert.Null(stored.ProcessedOnUtc);
        Assert.Null(stored.Error);
    }

    [Fact]
    public async Task ProcessBatch_RowDiscardedWhileItsDispatchSucceeds_KeepsTheDiscard()
    {
        var entered = new SemaphoreSlim(0);
        var release = new SemaphoreSlim(0);
        await using var serviceProvider = OutboxTestServices.Build(_ => new RecordingDispatcher(async (_, _) =>
        {
            entered.Release();
            await release.WaitAsync();
        }));
        var row = OutboxTestServices.Row(new OrderShipped(Guid.NewGuid()));
        row.RetryCount = 1;
        row.Error = "earlier failure";
        await OutboxTestServices.SeedAsync(serviceProvider, row);
        var worker = OutboxTestServices.Worker(serviceProvider);
        var discardedOnUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var batch = worker.ProcessBatchAsync(default);
        await entered.WaitAsync();
        await UpdateAsync(serviceProvider, row.Id, m => m.ProcessedOnUtc = discardedOnUtc);   // the discard
        release.Release();
        await batch;

        var stored = Assert.Single(await OutboxTestServices.RowsAsync(serviceProvider));
        Assert.Equal(discardedOnUtc, stored.ProcessedOnUtc);
        Assert.Equal("earlier failure", stored.Error);   // the discard's record of what failed survives
    }

    private static async Task UpdateAsync(IServiceProvider serviceProvider, Guid id, Action<OutboxMessage> change)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var tracked = await db.OutboxMessages.SingleAsync(m => m.Id == id);
        change(tracked);
        await db.SaveChangesAsync();
    }
}
