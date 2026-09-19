using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

/// <summary>
/// An OperationCanceledException that does not come from the stopping token (an HttpClient timeout in a handler,
/// say) is an ordinary failure, not a shutdown.
/// </summary>
public class OutboxWorkerCancellationTests
{
    private static RecordingDispatcher TimingOutOn<TEvent>() => new((e, _) =>
        e is TEvent ? throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.") : Task.CompletedTask);

    [Fact]
    public async Task ProcessBatch_HandlerTimesOut_IsRecordedAsAFailedAttemptAndTheNextRowIsDispatched()
    {
        var dispatcher = TimingOutOn<OrderShipped>();
        await using var serviceProvider = OutboxTestServices.Build(_ => dispatcher);
        await OutboxTestServices.SeedAsync(serviceProvider,
            OutboxTestServices.Row(new OrderShipped(Guid.NewGuid()), DateTime.UtcNow.AddSeconds(-2)),
            OutboxTestServices.Row(new OrderCancelled(Guid.NewGuid()), DateTime.UtcNow.AddSeconds(-1)));
        var worker = OutboxTestServices.Worker(serviceProvider);

        await worker.ProcessBatchAsync(default);

        var rows = await OutboxTestServices.RowsAsync(serviceProvider);
        Assert.Equal(1, rows[0].RetryCount);
        Assert.Contains(nameof(TaskCanceledException), rows[0].Error);
        Assert.NotNull(rows[1].ProcessedOnUtc);
        Assert.IsType<OrderCancelled>(Assert.Single(dispatcher.Dispatched));
    }

    [Fact]
    public async Task ExecuteAsync_HandlerTimesOut_TheDispatcherKeepsRunning()
    {
        var timeouts = 0;
        var dispatcher = new RecordingDispatcher((e, _) =>
        {
            if (e is not OrderShipped)
            {
                return Task.CompletedTask;
            }
            Interlocked.Increment(ref timeouts);
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
        });
        await using var serviceProvider = OutboxTestServices.Build(_ => dispatcher);
        await OutboxTestServices.SeedAsync(serviceProvider,
            OutboxTestServices.Row(new OrderShipped(Guid.NewGuid()), DateTime.UtcNow.AddSeconds(-2)),
            OutboxTestServices.Row(new OrderCancelled(Guid.NewGuid()), DateTime.UtcNow.AddSeconds(-1)));
        var worker = OutboxTestServices.Worker(serviceProvider);

        await worker.StartAsync(CancellationToken.None);
        bool retried;
        try
        {
            // Watched in memory: the test must not share the SQLite connection with the running worker.
            retried = await OutboxTestServices.EventuallyAsync(
                () => Task.FromResult(Volatile.Read(ref timeouts) >= 2), TimeSpan.FromSeconds(5));
            Assert.False(worker.ExecuteTask!.IsCompleted, $"the dispatcher stopped: {worker.ExecuteTask.Status}");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.True(retried, "the timed-out row should be retried on a later poll");
        var rows = await OutboxTestServices.RowsAsync(serviceProvider);
        Assert.True(rows[0].RetryCount >= 1);
        Assert.NotNull(rows[1].ProcessedOnUtc);
    }

    [Fact]
    public async Task ArchivalExecuteAsync_ArchiverTimesOut_IsCountedAsAFailureAndTheWorkerKeepsRunning()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<TestDbContext>());
        await using var serviceProvider = services.BuildServiceProvider();
        var archiver = new TimingOutArchiver();
        var worker = new OutboxArchivalHostedService(
            new OutboxArchivalOptions { PollingInterval = TimeSpan.FromMilliseconds(20) },
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new NullDistributedLock(),
            NullLogger<OutboxArchivalHostedService>.Instance,
            archiver);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var retried = await OutboxTestServices.EventuallyAsync(
                () => Task.FromResult(Volatile.Read(ref archiver.Calls) >= 3), TimeSpan.FromSeconds(5));

            Assert.True(retried, "archival should keep running after a timed-out batch");
            Assert.False(worker.ExecuteTask!.IsCompleted);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class TimingOutArchiver : IOutboxArchiver
    {
        public int Calls;

        public Task<int> ArchiveAsync(DbContext dbContext, DateTime cutoff, OutboxArchivalOptions options, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            throw new TaskCanceledException("blob upload timed out");
        }
    }
}
