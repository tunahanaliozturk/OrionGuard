using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionLock.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Moongazing.OrionGuard.Locks.Redis.Tests;

/// <summary>
/// End-to-end integration tests against a real Redis container. The fixture probes Docker
/// during initialization. When Docker is not reachable (developer box without Docker Desktop,
/// CI runner without the Docker engine) every test in this class skips via Xunit.SkippableFact's
/// `Skip.IfNot` instead of failing. CI's ubuntu-latest runner has Docker available so these
/// tests do run there.
/// </summary>
[Trait("Category", "Integration")]
public class OrionLockBridgeRedisIntegrationTests : IAsyncLifetime
{
    private RedisContainer? _redis;
    private ConnectionMultiplexer? _muxA;
    private ConnectionMultiplexer? _muxB;
    private string? _skipReason;

    public async Task InitializeAsync()
    {
        try
        {
            _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
            await _redis.StartAsync();
            var cs = _redis.GetConnectionString();
            _muxA = await ConnectionMultiplexer.ConnectAsync(cs);
            _muxB = await ConnectionMultiplexer.ConnectAsync(cs);
        }
        catch (Exception ex)
        {
            // Docker unreachable, image pull denied, or Redis failed to start.
            // Skip every test in this fixture instead of failing the suite.
            _skipReason = $"Docker / Redis container unavailable: {ex.GetType().Name}: {ex.Message}";
            if (_redis is not null)
            {
                try { await _redis.DisposeAsync(); } catch { /* swallow */ }
                _redis = null;
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_muxA is not null) await _muxA.DisposeAsync();
        if (_muxB is not null) await _muxB.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }

    private OrionLockBridgeDistributedLock NewBridge(IConnectionMultiplexer mux, string prefix = "test:")
    {
        var provider = new RedisLockProvider(mux, new RedisLockOptions { KeyPrefix = prefix });
        return new OrionLockBridgeDistributedLock(provider);
    }

    [SkippableFact]
    public async Task AcquireRelease_RoundTrip()
    {
        Skip.IfNot(_skipReason is null, _skipReason);

        var bridge = NewBridge(_muxA!);

        var handle = await bridge.TryAcquireAsync("rt", TimeSpan.FromSeconds(10));
        Assert.NotNull(handle);
        Assert.Equal("rt", handle!.LockKey);

        await handle.DisposeAsync();

        // After release, a fresh acquire must succeed.
        var again = await bridge.TryAcquireAsync("rt", TimeSpan.FromSeconds(10));
        Assert.NotNull(again);
        await again!.DisposeAsync();
    }

    [SkippableFact]
    public async Task CrossInstance_MutualExclusion()
    {
        Skip.IfNot(_skipReason is null, _skipReason);

        // Two independent bridge instances (modelling two replicas) over two independent
        // multiplexer connections to the SAME Redis must serialize on the same key.
        var bridgeA = NewBridge(_muxA!, "xinst:");
        var bridgeB = NewBridge(_muxB!, "xinst:");

        var handleA = await bridgeA.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(handleA);

        var handleB = await bridgeB.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.Null(handleB);

        await handleA!.DisposeAsync();

        var handleB2 = await bridgeB.TryAcquireAsync("k", TimeSpan.FromSeconds(30));
        Assert.NotNull(handleB2);
        await handleB2!.DisposeAsync();
    }

    [SkippableFact]
    public async Task LeaseExpiry_AllowsTakeover()
    {
        Skip.IfNot(_skipReason is null, _skipReason);

        var bridge = NewBridge(_muxA!, "expiry:");

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

    [SkippableFact]
    public async Task SatisfiesIDistributedLockContract()
    {
        Skip.IfNot(_skipReason is null, _skipReason);

        // Compile-time + runtime verification: the bridge is usable wherever
        // OrionGuard's IDistributedLock is required.
        IDistributedLock @lock = NewBridge(_muxA!, "contract:");

        var handle = await @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(5));
        Assert.NotNull(handle);
        Assert.IsAssignableFrom<IDistributedLockHandle>(handle);
        await handle!.DisposeAsync();
    }
}
