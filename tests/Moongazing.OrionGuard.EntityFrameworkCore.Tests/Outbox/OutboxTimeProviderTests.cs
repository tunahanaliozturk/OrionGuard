using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Locking;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

/// <summary>The dispatcher, the archival worker and the lock read the time from an injectable <see cref="TimeProvider"/>.</summary>
public class OutboxTimeProviderTests
{
    private static readonly DateTime Now = new(2030, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Dispatcher_StampsProcessedOnUtcFromTheTimeProvider()
    {
        await using var serviceProvider = OutboxTestServices.Build(_ => new RecordingDispatcher());
        await OutboxTestServices.SeedAsync(serviceProvider, OutboxTestServices.Row(new OrderShipped(Guid.NewGuid())));
        var worker = new OutboxDispatcherHostedService(
            serviceProvider.GetRequiredService<OutboxOptions>(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new NullDistributedLock(),
            typeMap: null,
            typeMapOptions: null,
            logger: null,
            wakeSignal: null,
            rowFailureObserver: null,
            timeProvider: new FixedTimeProvider(Now));

        await worker.ProcessBatchAsync(default);

        Assert.Equal(Now, Assert.Single(await OutboxTestServices.RowsAsync(serviceProvider)).ProcessedOnUtc);
    }

    [Fact]
    public async Task Archival_TakesTheRetentionCutoffAndTheLivenessTimestampFromTheTimeProvider()
    {
        await using var fixture = new ArchivalTestFixture();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArchivalTestDbContext>();
            db.Outbox.AddRange(
                new OutboxMessage { EventType = "test", Payload = "{}", OccurredOnUtc = Now.AddDays(-31), ProcessedOnUtc = Now.AddDays(-31) },
                new OutboxMessage { EventType = "test", Payload = "{}", OccurredOnUtc = Now.AddDays(-29), ProcessedOnUtc = Now.AddDays(-29) });
            await db.SaveChangesAsync();
        }
        var state = new OutboxArchivalState();
        var worker = new OutboxArchivalHostedService(
            new OutboxArchivalOptions { RetentionPeriod = TimeSpan.FromDays(30) },
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            new NullDistributedLock(),
            NullLogger<OutboxArchivalHostedService>.Instance,
            archiver: null,
            state,
            new FixedTimeProvider(Now));

        var archived = await worker.ArchiveBatchAsync(CancellationToken.None);

        Assert.Equal(1, archived);   // 31 days old by the injected clock; by the wall clock both rows are in the future
        Assert.Equal(Now, state.LastSuccessfulBatchUtc);
    }

    [Fact]
    public async Task SkipLockedDistributedLock_StampsTheLeaseFromTheTimeProvider()
    {
        await using var fixture = new LockingTestFixture();
        var @lock = new SkipLockedDistributedLock(
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SkipLockedDistributedLock>.Instance,
            new FixedTimeProvider(Now));

        var handle = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));

        Assert.NotNull(handle);
        using var scope = fixture.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<LockingTestDbContext>().Locks.AsNoTracking().SingleAsync();
        Assert.Equal(Now, row.AcquiredOnUtc);
        Assert.Equal(Now.AddSeconds(30), row.ExpiresOnUtc);
    }
}
