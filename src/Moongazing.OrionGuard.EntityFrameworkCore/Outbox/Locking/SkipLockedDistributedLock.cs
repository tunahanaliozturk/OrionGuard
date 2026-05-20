using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

/// <summary>
/// Default DB-backed <see cref="IDistributedLock"/> implementation. Uses an <see cref="OutboxLock"/>
/// row per lock key in the consumer's <c>OrionGuard_OutboxLocks</c> table. Provider-agnostic — all
/// SQL is issued through EF Core. Lease-based; expired holders are taken over by fresh callers.
/// </summary>
/// <remarks>
/// <para>
/// Concurrency contract: acquisition is correct but <b>best-effort and lease-bounded</b>, not a
/// hard mutual-exclusion primitive. The conditional <c>UPDATE ... WHERE (HolderId IS NULL OR
/// ExpiresOnUtc &lt;= now)</c> serializes concurrent acquirers via row locks on PostgreSQL, SQL
/// Server, and MySQL; the post-commit owner-check confirms which acquirer won. Under contention
/// the losers return <see langword="null"/> and retry on the next poll — brief lock starvation
/// (one polling cycle) is possible but never double-ownership of a fresh lease.
/// </para>
/// <para>
/// If a holder's lease expires before it disposes the handle (e.g. a batch outran
/// <c>LockLeaseDuration</c>), another caller may take over. Outbox dispatch is therefore
/// at-least-once: consumer event handlers must be idempotent. This is by design — see the
/// v6.4.0 design spec, section 6.6.
/// </para>
/// </remarks>
public sealed class SkipLockedDistributedLock : IDistributedLock
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SkipLockedDistributedLock>? _logger;
    private int _missingTableWarned;

    public SkipLockedDistributedLock(
        IServiceScopeFactory scopeFactory,
        ILogger<SkipLockedDistributedLock>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        string lockKey,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease must be > 0.");

        try
        {
            var holderId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var expires = now + leaseDuration;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<DbContext>();

            await using var tx = await ctx.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var updated = await ctx.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE OrionGuard_OutboxLocks
                      SET HolderId = {holderId}, AcquiredOnUtc = {now}, ExpiresOnUtc = {expires}
                    WHERE LockKey = {lockKey}
                      AND (HolderId IS NULL OR ExpiresOnUtc <= {now})",
                cancellationToken).ConfigureAwait(false);

            if (updated == 0)
            {
                try
                {
                    await ctx.Database.ExecuteSqlInterpolatedAsync(
                        $@"INSERT INTO OrionGuard_OutboxLocks (LockKey, HolderId, AcquiredOnUtc, ExpiresOnUtc)
                           SELECT {lockKey}, {holderId}, {now}, {expires}
                           WHERE NOT EXISTS (SELECT 1 FROM OrionGuard_OutboxLocks WHERE LockKey = {lockKey})",
                        cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException)
                {
                    await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return null;
                }
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

            // Why: authoritative arbiter for the INSERT race. When the conditional UPDATE matched
            // nothing we attempted an INSERT that inserts 0 rows if a concurrent caller won first;
            // reading HolderId back tells us definitively whether this caller owns the lock.
            var ownerCheck = await ctx.Set<OutboxLock>().AsNoTracking()
                .Where(x => x.LockKey == lockKey)
                .Select(x => x.HolderId)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (ownerCheck != holderId)
            {
                return null;
            }

            return new Handle(this, lockKey, holderId);
        }
        catch (Exception ex) when (IsMissingTable(ex))
        {
            LogMissingTableOnce();
            return null;
        }
    }

    private async Task ReleaseAsync(string lockKey, Guid holderId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<DbContext>();
            var now = DateTime.UtcNow;
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE OrionGuard_OutboxLocks
                      SET HolderId = NULL, ExpiresOnUtc = {now}
                    WHERE LockKey = {lockKey} AND HolderId = {holderId}",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsMissingTable(ex))
        {
            // table dropped between acquire and release — nothing to clean up.
        }
    }

    private static bool IsMissingTable(Exception ex)
    {
        var msg = ex.Message;
        if (string.IsNullOrEmpty(msg)) return false;
        return msg.Contains("no such table", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase);
    }

    private void LogMissingTableOnce()
    {
        if (Interlocked.Exchange(ref _missingTableWarned, 1) == 0)
        {
            _logger?.LogWarning(
                "OrionGuard_OutboxLocks table not found. Distributed locking is disabled until the v6.4.0 migration is applied. " +
                "Single-instance consumers who do not want this migration should call opts.UseDistributedLock<NullDistributedLock>().");
        }
    }

    private sealed class Handle(SkipLockedDistributedLock owner, string lockKey, Guid holderId) : IDistributedLockHandle
    {
        public string LockKey => lockKey;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await owner.ReleaseAsync(lockKey, holderId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                owner._logger?.LogWarning(ex,
                    "Failed to release distributed lock '{LockKey}'. Lease will expire naturally.",
                    lockKey);
            }
        }
    }
}
