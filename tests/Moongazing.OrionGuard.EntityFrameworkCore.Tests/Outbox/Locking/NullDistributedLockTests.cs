using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Locking;

public class NullDistributedLockTests
{
    [Fact]
    public async Task TryAcquireAsync_ShouldAlwaysReturnHandle()
    {
        var @lock = new NullDistributedLock();
        var handle = await @lock.TryAcquireAsync("k", TimeSpan.FromMinutes(1));
        Assert.NotNull(handle);
        Assert.Equal("k", handle!.LockKey);
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldReturnHandleEvenOnRepeatedCalls()
    {
        var @lock = new NullDistributedLock();
        var first = await @lock.TryAcquireAsync("k", TimeSpan.FromMinutes(1));
        var second = await @lock.TryAcquireAsync("k", TimeSpan.FromMinutes(1));
        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task DisposeAsync_ShouldNotThrow()
    {
        var @lock = new NullDistributedLock();
        var handle = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(1));
        await handle!.DisposeAsync();
    }
}
