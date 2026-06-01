using System.Collections.Concurrent;
using Moongazing.OrionLock.Providers;

namespace Moongazing.OrionGuard.Locks.Redis.Tests;

/// <summary>
/// In-memory <see cref="IDistributedLockProvider"/> with the same owner-token + lease-expiry
/// semantics as the Redis backend. Used to unit-test the bridge adapter without Docker.
/// </summary>
internal sealed class FakeDistributedLockProvider : IDistributedLockProvider
{
    private readonly ConcurrentDictionary<string, Lease> _leases = new();
    private readonly Func<DateTimeOffset> _now;

    public FakeDistributedLockProvider(Func<DateTimeOffset>? clock = null)
    {
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int ReleaseCallCount { get; private set; }

    public Task<bool> TryAcquireAsync(string key, string ownerToken, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        var newLease = new Lease(ownerToken, _now() + lease);
        var won = _leases.AddOrUpdate(
            key,
            _ => newLease,
            (_, existing) => existing.ExpiresAt <= _now() ? newLease : existing) == newLease;
        return Task.FromResult(won);
    }

    public Task<bool> TryRenewAsync(string key, string ownerToken, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        if (_leases.TryGetValue(key, out var current)
            && current.Owner == ownerToken
            && current.ExpiresAt > _now())
        {
            _leases[key] = current with { ExpiresAt = _now() + lease };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task ReleaseAsync(string key, string ownerToken, CancellationToken cancellationToken = default)
    {
        ReleaseCallCount++;
        if (_leases.TryGetValue(key, out var current) && current.Owner == ownerToken)
        {
            _leases.TryRemove(new KeyValuePair<string, Lease>(key, current));
        }
        return Task.CompletedTask;
    }

    private sealed record Lease(string Owner, DateTimeOffset ExpiresAt);
}
