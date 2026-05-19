using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Locking;

public class SkipLockedDistributedLockTests : IAsyncLifetime
{
    private LockingTestFixture _fx = default!;

    public Task InitializeAsync() { _fx = new LockingTestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _fx.DisposeAsync();

    private SkipLockedDistributedLock NewLock() =>
        new(_fx.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<SkipLockedDistributedLock>.Instance);

    [Fact]
    public async Task TryAcquireAsync_ShouldReturnHandle_WhenSlotIsFree()
    {
        var @lock = NewLock();
        var handle = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(handle);
        Assert.Equal("k", handle!.LockKey);
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldReturnNull_WhenSlotIsHeld()
    {
        var @lock = NewLock();
        var first = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);

        var second = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.Null(second);
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldSucceedAgain_AfterFirstHandleIsDisposed()
    {
        var @lock = NewLock();
        var first = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);
        await first!.DisposeAsync();

        var second = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(second);
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldTakeOver_WhenLeaseHasExpired()
    {
        var @lock = NewLock();
        var first = await @lock.TryAcquireAsync("k", TimeSpan.FromMilliseconds(50));
        Assert.NotNull(first);

        await Task.Delay(150);

        var second = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(second);
    }

    [Fact]
    public async Task DisposingExpiredHandle_ShouldBeNoOp_WhenAnotherHolderHasTakenOver()
    {
        var @lock = NewLock();
        var first = await @lock.TryAcquireAsync("k", TimeSpan.FromMilliseconds(50));
        Assert.NotNull(first);
        await Task.Delay(150);

        var second = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(second);

        await first!.DisposeAsync();

        var third = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.Null(third);
    }

    private sealed class NoLockTableDbContext(DbContextOptions<NoLockTableDbContext> options) : DbContext(options)
    {
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldReturnNull_WhenLockTableIsMissing()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<NoLockTableDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<NoLockTableDbContext>());
        await using var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<NoLockTableDbContext>();
            await ctx.Database.EnsureCreatedAsync();
        }

        var @lock = new SkipLockedDistributedLock(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SkipLockedDistributedLock>.Instance);

        var handle = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.Null(handle);

        var handle2 = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.Null(handle2);
    }
}
