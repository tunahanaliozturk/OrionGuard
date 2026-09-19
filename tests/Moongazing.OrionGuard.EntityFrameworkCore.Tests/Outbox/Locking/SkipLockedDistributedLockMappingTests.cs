using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Locking;

/// <summary>The lock runs its SQL against the table the model maps, and reads a lost INSERT race as contention.</summary>
public class SkipLockedDistributedLockMappingTests
{
    [Fact]
    public async Task TryAcquireAsync_OutboxLockMappedToAnotherTable_UsesThatTable()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = BuildServices<RenamedLockTableDbContext>(connection);
        var @lock = new SkipLockedDistributedLock(serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SkipLockedDistributedLock>.Instance);

        var first = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        var second = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));

        Assert.NotNull(first);
        Assert.Null(second);
        await using var scope = serviceProvider.CreateAsyncScope();
        var holder = await scope.ServiceProvider.GetRequiredService<RenamedLockTableDbContext>().Set<OutboxLock>()
            .AsNoTracking().SingleAsync();
        Assert.Equal("k", holder.LockKey);
    }

    [Fact]
    public async Task TryAcquireAsync_InsertRaceLostWithAProviderDbException_ReturnsNullAndRecordsContention()
    {
        long contended = 0;
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.lock_contended")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref contended, value));
        listener.Start();

        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = BuildServices<LockingTestDbContext>(connection, new ConcurrentInsertWinsInterceptor());
        var @lock = new SkipLockedDistributedLock(serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SkipLockedDistributedLock>.Instance);

        // Raw SQL surfaces the provider's own DbException (here SqliteException), never a DbUpdateException.
        var handle = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));

        Assert.Null(handle);
        Assert.True(Interlocked.Read(ref contended) >= 1);
    }

    [Fact]
    public async Task TryAcquireAsync_LockTableMappedButNotCreated_ReturnsNull()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddDbContext<LockingTestDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<LockingTestDbContext>());
        await using var serviceProvider = services.BuildServiceProvider();   // no EnsureCreated: the migration was not applied
        var @lock = new SkipLockedDistributedLock(serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SkipLockedDistributedLock>.Instance);

        Assert.Null(await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30)));
        Assert.Null(await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30)));
    }

    private static ServiceProvider BuildServices<TContext>(SqliteConnection connection, params IInterceptor[] interceptors)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddDbContext<TContext>(o => o.UseSqlite(connection).AddInterceptors(interceptors));
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<TContext>());
        var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TContext>().Database.EnsureCreated();
        return serviceProvider;
    }

    /// <summary>Another replica inserted the same lock key first: the INSERT fails with the provider's unique violation.</summary>
    private sealed class ConcurrentInsertWinsInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                throw new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: OrionGuard_OutboxLocks.LockKey'.", 19);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class RenamedLockTableDbContext : DbContext
    {
        public RenamedLockTableDbContext(DbContextOptions<RenamedLockTableDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<OutboxLock>(b =>
            {
                new OutboxLockEntityTypeConfiguration().Configure(b);
                b.ToTable("ops_locks");
            });
    }
}
