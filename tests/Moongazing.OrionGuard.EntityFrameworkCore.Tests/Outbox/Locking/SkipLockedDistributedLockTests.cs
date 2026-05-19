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
}
