namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

/// <summary>
/// Persistent row backing <c>SkipLockedDistributedLock</c>. Each lock key is one row in
/// <c>OrionGuard_OutboxLocks</c>. <see cref="HolderId"/> is null when the lock is free; otherwise
/// the GUID of the current owner. <see cref="ExpiresOnUtc"/> is the lease deadline; if it is in
/// the past, any caller may take over.
/// </summary>
public sealed class OutboxLock
{
    public string LockKey { get; set; } = default!;
    public Guid? HolderId { get; set; }
    public DateTime AcquiredOnUtc { get; set; }
    public DateTime ExpiresOnUtc { get; set; }
}
