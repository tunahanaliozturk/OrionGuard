namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

/// <summary>
/// Lease handle returned by <see cref="IDistributedLock.TryAcquireAsync"/>. Disposing releases
/// the lock (best-effort — release is a no-op if the lease has already expired and another
/// holder has taken over).
/// </summary>
public interface IDistributedLockHandle : IAsyncDisposable
{
    /// <summary>The lock key this handle holds.</summary>
    string LockKey { get; }
}
