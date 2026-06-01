namespace Moongazing.OrionGuard.Locks.Redis.Tests;

public class OrionLockBridgeDistributedLockUnitTests
{
    [Fact]
    public async Task TryAcquireAsync_ShouldReturnHandle_WhenProviderGrantsLease()
    {
        var provider = new FakeDistributedLockProvider();
        var bridge = new OrionLockBridgeDistributedLock(provider);

        var handle = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(30));

        Assert.NotNull(handle);
        Assert.Equal("k", handle!.LockKey);
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldReturnNull_WhenLockAlreadyHeld()
    {
        var provider = new FakeDistributedLockProvider();
        var bridge = new OrionLockBridgeDistributedLock(provider);

        var first = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        var second = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(30));

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldReacquire_AfterFirstHandleDisposed()
    {
        var provider = new FakeDistributedLockProvider();
        var bridge = new OrionLockBridgeDistributedLock(provider);

        var first = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        await first!.DisposeAsync();

        var second = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(30));

        Assert.NotNull(second);
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotent()
    {
        var provider = new FakeDistributedLockProvider();
        var bridge = new OrionLockBridgeDistributedLock(provider);

        var handle = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        await handle!.DisposeAsync();
        await handle.DisposeAsync();

        Assert.Equal(1, provider.ReleaseCallCount);
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldThrow_OnNullOrWhitespaceKey()
    {
        var bridge = new OrionLockBridgeDistributedLock(new FakeDistributedLockProvider());

        await Assert.ThrowsAsync<ArgumentException>(() => bridge.TryAcquireAsync("", TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<ArgumentException>(() => bridge.TryAcquireAsync("   ", TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => bridge.TryAcquireAsync(null!, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldThrow_OnNonPositiveLease()
    {
        var bridge = new OrionLockBridgeDistributedLock(new FakeDistributedLockProvider());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => bridge.TryAcquireAsync("k", TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Constructor_ShouldThrow_OnNullProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new OrionLockBridgeDistributedLock(null!));
    }

    [Fact]
    public async Task TryAcquireAsync_ShouldUseDistinctOwnerTokens_PerCall()
    {
        // Verifies the bridge generates a fresh owner-token per acquisition so a stale
        // handle cannot release a successor's lease.
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var provider = new FakeDistributedLockProvider(() => clock.Now);
        var bridge = new OrionLockBridgeDistributedLock(provider);

        var first = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(1));
        Assert.NotNull(first);

        // Let the lease expire; second acquire must succeed with a new owner token.
        clock.Advance(TimeSpan.FromSeconds(5));
        var second = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(second);

        // Disposing the first (expired) handle must NOT release the second's lease.
        await first!.DisposeAsync();

        // Provider still holds the second lease for the same key.
        var third = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.Null(third);

        await second!.DisposeAsync();
    }

    private sealed class ManualClock
    {
        public ManualClock(DateTimeOffset start) { Now = start; }
        public DateTimeOffset Now { get; private set; }
        public void Advance(TimeSpan delta) => Now += delta;
    }
}
