using System.Data.Common;
using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Locking;

// Not parallel: lock_contended is a process-wide counter, and these tests assert it is NOT recorded.
[CollectionDefinition(nameof(SkipLockedDistributedLockInsertFailureTests), DisableParallelization = true)]
public sealed class SkipLockedDistributedLockInsertFailureCollection
{
}

/// <summary>
/// Only a lost INSERT race is contention. Any other failure of the lock's INSERT (here a NOT NULL column the lock
/// does not fill; on SQL Server or PostgreSQL also a key longer than the column, or a transient fault) must reach
/// the dispatcher as a failed poll instead of silently parking it as a standby replica.
/// </summary>
[Collection(nameof(SkipLockedDistributedLockInsertFailureTests))]
public sealed class SkipLockedDistributedLockInsertFailureTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Filename=:memory:");
    private ServiceProvider serviceProvider = default!;
    private MeterListener listener = default!;
    private long lockContended;
    private long batchFaults;

    public async Task InitializeAsync()
    {
        listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name is "orionguard.outbox.dispatcher.lock_contended" or "orionguard.outbox.dispatcher.batch_faults")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "orionguard.outbox.dispatcher.lock_contended")
            {
                Interlocked.Add(ref lockContended, value);
            }
            else
            {
                Interlocked.Add(ref batchFaults, value);
            }
        });
        listener.Start();

        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<RequiredColumnLockDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<RequiredColumnLockDbContext>());
        serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RequiredColumnLockDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        listener.Dispose();
        await serviceProvider.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_InsertFailsForAReasonOtherThanARace_ThrowsInsteadOfReportingContention()
    {
        var @lock = new SkipLockedDistributedLock(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SkipLockedDistributedLock>.Instance);

        var thrown = await Assert.ThrowsAnyAsync<DbException>(() => @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30)));

        Assert.Contains("NOT NULL", thrown.Message);
        Assert.Equal(0, Interlocked.Read(ref lockContended));
    }

    [Fact]
    public async Task Dispatcher_LockInsertFailsForAReasonOtherThanARace_LogsAFailedPollInsteadOfIdlingAsStandby()
    {
        var logger = new ListLogger<OutboxDispatcherHostedService>();
        var worker = new OutboxDispatcherHostedService(
            new OutboxOptions { PollingInterval = TimeSpan.FromMilliseconds(20) },
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new SkipLockedDistributedLock(serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SkipLockedDistributedLock>.Instance),
            logger: logger);

        await worker.StartAsync(CancellationToken.None);
        bool logged;
        try
        {
            logged = await OutboxTestServices.EventuallyAsync(
                () => Task.FromResult(logger.Entries.Any(e => e.Level == LogLevel.Error && e.Exception is DbException)),
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.True(logged, "the lock's INSERT failure must surface as a logged failed poll");
        Assert.True(Interlocked.Read(ref batchFaults) >= 1);
        Assert.Equal(0, Interlocked.Read(ref lockContended));
    }

    /// <summary>A lock table with an extra required column, which the lock's INSERT leaves NULL.</summary>
    private sealed class RequiredColumnLockDbContext : DbContext
    {
        public RequiredColumnLockDbContext(DbContextOptions<RequiredColumnLockDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<OutboxLock>(b =>
            {
                new OutboxLockEntityTypeConfiguration().Configure(b);
                b.Property<string>("Region").IsRequired();
            });
    }
}
