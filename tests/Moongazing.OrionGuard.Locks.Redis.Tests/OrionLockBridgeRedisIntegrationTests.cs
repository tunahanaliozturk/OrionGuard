using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionLock.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Moongazing.OrionGuard.Locks.Redis.Tests;

/// <summary>
/// End-to-end integration tests against a real Redis container. Skipped automatically when
/// Docker is unavailable on the host (CI without docker, dev box without Docker Desktop).
/// </summary>
[Trait("Category", "Integration")]
public class OrionLockBridgeRedisIntegrationTests : IAsyncLifetime
{
    private RedisContainer _redis = default!;
    private ConnectionMultiplexer _muxA = default!;
    private ConnectionMultiplexer _muxB = default!;

    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
        await _redis.StartAsync();
        var cs = _redis.GetConnectionString();
        _muxA = await ConnectionMultiplexer.ConnectAsync(cs);
        _muxB = await ConnectionMultiplexer.ConnectAsync(cs);
    }

    public async Task DisposeAsync()
    {
        await _muxA.DisposeAsync();
        await _muxB.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private OrionLockBridgeDistributedLock NewBridge(IConnectionMultiplexer mux, string prefix = "test:")
    {
        var provider = new RedisLockProvider(mux, new RedisLockOptions { KeyPrefix = prefix });
        return new OrionLockBridgeDistributedLock(provider);
    }

    [Fact]
    public async Task AcquireRelease_RoundTrip()
    {
        var bridge = NewBridge(_muxA);

        var handle = await bridge.TryAcquireAsync("rt", TimeSpan.FromSeconds(10));
        Assert.NotNull(handle);
        Assert.Equal("rt", handle!.LockKey);

        await handle.DisposeAsync();

        // After release, a fresh acquire must succeed.
        var again = await bridge.TryAcquireAsync("rt", TimeSpan.FromSeconds(10));
        Assert.NotNull(again);
        await again!.DisposeAsync();
    }

    [Fact]
    public async Task CrossInstance_MutualExclusion()
    {
        // Two independent bridge instances (modelling two replicas) over two independent
        // multiplexer connections to the SAME Redis must serialize on the same key.
        var bridgeA = NewBridge(_muxA, "xinst:");
        var bridgeB = NewBridge(_muxB, "xinst:");

        var handleA = await bridgeA.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(handleA);

        var handleB = await bridgeB.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.Null(handleB);

        await handleA!.DisposeAsync();

        var handleB2 = await bridgeB.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(handleB2);
        await handleB2!.DisposeAsync();
    }

    [Fact]
    public async Task LeaseExpiry_AllowsTakeover()
    {
        var bridge = NewBridge(_muxA, "expiry:");

        var first = await bridge.TryAcquireAsync("k", TimeSpan.FromMilliseconds(500));
        Assert.NotNull(first);

        // Wait past the lease.
        await Task.Delay(TimeSpan.FromSeconds(1));

        var second = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(second);

        // Disposing the expired first handle must not release the second's lease
        // (release is owner-checked via Lua CAS on the Redis backend).
        await first!.DisposeAsync();

        var third = await bridge.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.Null(third);

        await second!.DisposeAsync();
    }

    [Fact]
    public async Task SatisfiesIDistributedLockContract()
    {
        // Compile-time + runtime verification: the bridge is usable wherever
        // OrionGuard's IDistributedLock is required.
        IDistributedLock @lock = NewBridge(_muxA, "contract:");

        var handle = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(5));
        Assert.NotNull(handle);
        Assert.IsAssignableFrom<IDistributedLockHandle>(handle);
        await handle!.DisposeAsync();
    }
}
