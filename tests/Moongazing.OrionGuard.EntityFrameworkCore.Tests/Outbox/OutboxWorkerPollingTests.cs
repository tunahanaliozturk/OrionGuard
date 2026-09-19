using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

/// <summary>How the dispatcher and archival workers pace their polls, and what a failed poll leaves behind.</summary>
public class OutboxWorkerPollingTests
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    [Fact]
    public async Task DispatcherExecuteAsync_BacklogLargerThanBatchSize_IsDrainedWithoutWaitingForThePollingInterval()
    {
        var dispatcher = new RecordingDispatcher();
        await using var serviceProvider = OutboxTestServices.Build(_ => dispatcher, configureOutbox: o =>
        {
            o.BatchSize = 2;
            o.PollingInterval = OneHour;   // only draining can dispatch everything within the test's timeout
        });
        await OutboxTestServices.SeedAsync(serviceProvider, Enumerable.Range(0, 5)
            .Select(i => (object)OutboxTestServices.Row(new OrderShipped(Guid.NewGuid()), DateTime.UtcNow.AddSeconds(i - 10)))
            .ToArray());
        var worker = OutboxTestServices.Worker(serviceProvider);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Watched in memory: the test must not share the SQLite connection with the running worker.
            var drained = await OutboxTestServices.EventuallyAsync(
                () => Task.FromResult(dispatcher.Dispatched.Count == 5), TimeSpan.FromSeconds(5));

            Assert.True(drained, $"dispatched {dispatcher.Dispatched.Count} of 5 rows");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
        Assert.All(await OutboxTestServices.RowsAsync(serviceProvider), r => Assert.NotNull(r.ProcessedOnUtc));
    }

    [Fact]
    public async Task DispatcherExecuteAsync_FullBatchWithAFailure_WaitsForThePollingIntervalBeforeRetrying()
    {
        // A full batch whose rows fail must not be re-polled at once: that would spend every retry in seconds.
        var attempts = 0;
        await using var serviceProvider = OutboxTestServices.Build(
            _ => new RecordingDispatcher((_, _) =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("downstream 503");
            }),
            configureOutbox: o =>
            {
                o.BatchSize = 2;
                o.MaxRetries = 5;
                o.PollingInterval = OneHour;
            });
        await OutboxTestServices.SeedAsync(serviceProvider,
            OutboxTestServices.Row(new OrderShipped(Guid.NewGuid())),
            OutboxTestServices.Row(new OrderShipped(Guid.NewGuid())));
        var worker = OutboxTestServices.Worker(serviceProvider);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Watched in memory: the test must not share the SQLite connection with the running worker.
            await OutboxTestServices.EventuallyAsync(() => Task.FromResult(Volatile.Read(ref attempts) >= 2), TimeSpan.FromSeconds(5));
            await Task.Delay(300);

            Assert.Equal(2, Volatile.Read(ref attempts));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
        Assert.All(await OutboxTestServices.RowsAsync(serviceProvider), r => Assert.Equal(1, r.RetryCount));
    }

    [Fact]
    public async Task DispatcherExecuteAsync_PollFails_TheErrorIsLoggedAndCountedAsABatchFault()
    {
        long faults = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.batch_faults")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref faults, value));
        listener.Start();

        // No DbContext is registered, so every poll fails before it reaches any row.
        await using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var logger = new ListLogger<OutboxDispatcherHostedService>();
        var worker = new OutboxDispatcherHostedService(
            new OutboxOptions { PollingInterval = TimeSpan.FromMilliseconds(20) },
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new NullDistributedLock(),
            logger: logger);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var logged = await OutboxTestServices.EventuallyAsync(
                () => Task.FromResult(logger.Entries.Any(e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException)),
                TimeSpan.FromSeconds(5));

            Assert.True(logged, "a failed poll must be logged, not swallowed");
            Assert.True(Interlocked.Read(ref faults) >= 1);
            Assert.False(worker.ExecuteTask!.IsCompleted);   // and the worker keeps polling
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ArchivalExecuteAsync_BacklogLargerThanBatchSize_IsArchivedWithoutWaitingForThePollingInterval()
    {
        await using var fixture = new ArchivalTestFixture();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArchivalTestDbContext>();
            db.Outbox.AddRange(Enumerable.Range(0, 5).Select(i => new OutboxMessage
            {
                EventType = "test",
                Payload = "{}",
                OccurredOnUtc = DateTime.UtcNow.AddDays(-45),
                ProcessedOnUtc = DateTime.UtcNow.AddDays(-45).AddSeconds(i),
            }));
            await db.SaveChangesAsync();
        }
        var archiver = new CountingArchiver();
        var worker = new OutboxArchivalHostedService(
            new OutboxArchivalOptions { BatchSize = 2, PollingInterval = OneHour },
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new NullDistributedLock(),
            NullLogger<OutboxArchivalHostedService>.Instance,
            archiver);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Watched in memory: the test must not share the SQLite connection with the running worker.
            var archived = await OutboxTestServices.EventuallyAsync(
                () => Task.FromResult(Volatile.Read(ref archiver.Archived) == 5), TimeSpan.FromSeconds(5));

            Assert.True(archived, $"archived {Volatile.Read(ref archiver.Archived)} of the 5 rows past retention within one polling interval");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
        using var check = fixture.Services.CreateScope();
        Assert.Equal(0, await check.ServiceProvider.GetRequiredService<ArchivalTestDbContext>().Outbox.CountAsync());
    }

    private sealed class CountingArchiver : IOutboxArchiver
    {
        private readonly DeleteOutboxArchiver inner = new();
        public int Archived;

        public async Task<int> ArchiveAsync(DbContext dbContext, DateTime cutoff, OutboxArchivalOptions options, CancellationToken cancellationToken)
        {
            var archived = await inner.ArchiveAsync(dbContext, cutoff, options, cancellationToken);
            Interlocked.Add(ref Archived, archived);
            return archived;
        }
    }
}
