using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;
using Moongazing.OrionLock.Providers;

namespace Moongazing.OrionGuard.Locks.Redis;

/// <summary>
/// Bridges OrionGuard's <see cref="IDistributedLock"/> contract to an OrionLock
/// <see cref="IDistributedLockProvider"/> (Redis-backed via <c>Moongazing.OrionLock.Redis</c>).
/// Lets the outbox dispatcher hosted service introduced in OrionGuard v6.4.0 coordinate
/// across replicas through Redis instead of the default <c>OrionGuard_OutboxLocks</c> DB table.
/// </summary>
/// <remarks>
/// <para>
/// Why bridge against the raw <see cref="IDistributedLockProvider"/> instead of OrionLock's
/// higher-level <c>IDistributedLock</c>? OrionGuard's contract requires acquisition to be
/// non-blocking and return <see langword="null"/> immediately on contention. OrionLock's
/// high-level lock layers blocking-acquire retry, watchdog lease renewal, and same-process
/// reentrancy on top of the provider. Those features are useful in isolation but conflict
/// with OrionGuard's outbox semantics: the dispatcher polls on its own cadence, treats a
/// missed lock as "another replica is dispatching, try next tick", and intentionally lets
/// a lease expire so a crashed replica's lock is reclaimed. Going against the provider
/// directly keeps semantics aligned and avoids surprising behaviour.
/// </para>
/// <para>
/// Handle-semantics mapping: OrionGuard's handle exposes <c>LockKey</c> + <c>IAsyncDisposable</c>.
/// OrionLock's high-level handle exposes <c>IsHeld</c> and a <c>LostToken</c> cancelled on
/// renewal failure. The bridge does not surface lease-loss upward because OrionGuard's outbox
/// dispatcher is already at-least-once by design (v6.4.0 spec section 6.6) and event handlers
/// must be idempotent. The watchdog isn't started either — the dispatcher's lease duration
/// is already sized to outlast one polling cycle.
/// </para>
/// <para>
/// Release uses an owner-token check (Lua compare-and-delete on the Redis backend), so an
/// expired lease that another replica has since taken over will not be released by this
/// handle's disposal.
/// </para>
/// </remarks>
/// <seealso href="https://github.com/tunahanaliozturk/OrionGuard">OrionGuard</seealso>
/// <seealso href="https://github.com/tunahanaliozturk/OrionLock">OrionLock</seealso>
public sealed class OrionLockBridgeDistributedLock : IDistributedLock
{
    private readonly IDistributedLockProvider _provider;

    /// <summary>Creates a bridge over the supplied OrionLock provider.</summary>
    /// <param name="provider">The OrionLock backend provider (e.g. <c>RedisLockProvider</c>).</param>
    public OrionLockBridgeDistributedLock(IDistributedLockProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <inheritdoc />
    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        string lockKey,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease must be > 0.");
        }

        // Owner token is per-acquisition; the bridge releases only if it still owns the key.
        var ownerToken = Guid.NewGuid().ToString("N");

        var acquired = await _provider
            .TryAcquireAsync(lockKey, ownerToken, leaseDuration, cancellationToken)
            .ConfigureAwait(false);

        if (!acquired)
        {
            return null;
        }

        return new BridgeHandle(_provider, lockKey, ownerToken);
    }

    /// <summary>
    /// OrionGuard handle adapter. Disposing issues an owner-checked release against the
    /// underlying OrionLock provider.
    /// </summary>
    private sealed class BridgeHandle : IDistributedLockHandle
    {
        private readonly IDistributedLockProvider _provider;
        private readonly string _ownerToken;
        private int _disposed;

        public BridgeHandle(IDistributedLockProvider provider, string lockKey, string ownerToken)
        {
            _provider = provider;
            LockKey = lockKey;
            _ownerToken = ownerToken;
        }

        public string LockKey { get; }

        public async ValueTask DisposeAsync()
        {
            // Guard against double-dispose: a second release would still be a no-op on the
            // backend (owner-checked) but issuing a second call is wasteful and could race
            // with a successor acquisition under heavy contention.
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await _provider.ReleaseAsync(LockKey, _ownerToken, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort release: the lease will expire naturally if the backend is
                // unreachable. The outbox dispatcher tolerates lease loss by design.
            }
        }
    }
}
