namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

/// <summary>
/// No-op implementation that always acquires the lock and never blocks. Useful for single-instance
/// consumers who do not want to apply the v6.4.0 <c>OrionGuard_OutboxLocks</c> migration. Wire via
/// <c>opts.UseOutbox(...).UseDistributedLock&lt;NullDistributedLock&gt;()</c>.
/// </summary>
public sealed class NullDistributedLock : IDistributedLock
{
    public Task<IDistributedLockHandle?> TryAcquireAsync(
        string lockKey,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IDistributedLockHandle?>(new Handle(lockKey));

    private sealed class Handle(string lockKey) : IDistributedLockHandle
    {
        public string LockKey => lockKey;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
