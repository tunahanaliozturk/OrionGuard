namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

/// <summary>
/// Acquires named distributed leases used by outbox dispatcher and archival workers.
/// Implementations MUST be non-blocking — if the lock is held, return <see langword="null"/>
/// immediately rather than waiting.
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Tries to acquire the lock identified by <paramref name="lockKey"/>. Returns a handle on
    /// success; <see langword="null"/> when another holder owns the lock.
    /// </summary>
    /// <param name="lockKey">Logical lock identifier.</param>
    /// <param name="leaseDuration">
    /// Maximum time the caller intends to hold the lock. Lease expiry releases the lock for other
    /// holders even if the original owner crashes without disposing the handle.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IDistributedLockHandle?> TryAcquireAsync(
        string lockKey,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
}
