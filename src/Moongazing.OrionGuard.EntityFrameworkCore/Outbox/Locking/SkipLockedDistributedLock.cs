using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

/// <summary>
/// Default DB-backed <see cref="IDistributedLock"/> implementation. Uses an <see cref="OutboxLock"/>
/// row per lock key in the consumer's <c>OrionGuard_OutboxLocks</c> table. Provider-agnostic: the table and
/// column names come from the <see cref="OutboxLock"/> mapping in the EF Core model and are delimited by the
/// provider, and every value is sent as a parameter. Lease-based; expired holders are taken over by fresh callers.
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
    private readonly TimeProvider _timeProvider;
    private int _missingTableWarned;

    public SkipLockedDistributedLock(
        IServiceScopeFactory scopeFactory,
        ILogger<SkipLockedDistributedLock>? logger = null)
        : this(scopeFactory, logger, timeProvider: null)
    {
    }

    /// <summary>Creates the lock with the clock used for lease timestamps.</summary>
    /// <param name="scopeFactory">Factory for the DI scopes that resolve the <see cref="DbContext"/>.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="timeProvider">Clock for acquisition and expiry times; <see langword="null"/> means <see cref="TimeProvider.System"/>.</param>
    public SkipLockedDistributedLock(
        IServiceScopeFactory scopeFactory,
        ILogger<SkipLockedDistributedLock>? logger,
        TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        string lockKey,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease must be > 0.");

        LockStatements? statements = null;
        try
        {
            var holderId = Guid.NewGuid();
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var expires = now + leaseDuration;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();
            statements = LockStatements.For(db);
            if (statements is null)
            {
                LogUnavailableOnce(null, $"{nameof(OutboxLock)} is not mapped on {db.GetType().Name}; apply {nameof(OutboxLockEntityTypeConfiguration)} in OnModelCreating");
                return null;
            }

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var updated = await db.Database.ExecuteSqlRawAsync(
                statements.Acquire, new object[] { holderId, now, expires, lockKey }, cancellationToken).ConfigureAwait(false);

            if (updated == 0)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(
                        statements.Insert, new object[] { lockKey, holderId, now, expires }, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is DbException or DbUpdateException)
                {
                    // Raw SQL surfaces the provider's DbException (a unique-key violation) when a concurrent
                    // caller won the INSERT race: genuine contention.
                    await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    OutboxDispatcherDiagnostics.RecordLockContended();
                    return null;
                }
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

            // Why: authoritative arbiter for the INSERT race. When the conditional UPDATE matched
            // nothing we attempted an INSERT that inserts 0 rows if a concurrent caller won first;
            // reading HolderId back tells us definitively whether this caller owns the lock.
            var ownerCheck = await db.Set<OutboxLock>().AsNoTracking()
                .Where(x => x.LockKey == lockKey)
                .Select(x => x.HolderId)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (ownerCheck != holderId)
            {
                // Another holder owns the lease: genuine contention.
                OutboxDispatcherDiagnostics.RecordLockContended();
                return null;
            }

            return new Handle(this, lockKey, holderId);
        }
        catch (Exception ex) when (statements is not null && IsMissingTable(ex, statements.TableName))
        {
            // NOT contention: the lock table is missing (migration not applied). Deliberately does
            // NOT record lock_contended so the counter stays a clean standby-vs-active signal
            // rather than masking a broken dispatcher setup as healthy contention.
            LogUnavailableOnce(ex, $"table {statements.TableName} was not found; apply the v6.4.0 migration");
            return null;
        }
    }

    private async Task ReleaseAsync(string lockKey, Guid holderId, CancellationToken cancellationToken = default)
    {
        LockStatements? statements = null;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DbContext>();
            statements = LockStatements.For(db);
            if (statements is null)
            {
                return;
            }
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await db.Database.ExecuteSqlRawAsync(
                statements.Release, new object[] { now, lockKey, holderId }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (statements is not null && IsMissingTable(ex, statements.TableName))
        {
            // table dropped between acquire and release — nothing to clean up.
        }
    }

    // Every provider names the missing table in its error (PostgreSQL: relation "x" does not exist; SQL Server:
    // Invalid object name 'x'; SQLite: no such table: x; MySQL: Table 'db.x' doesn't exist), so an unrelated
    // "does not exist" error is not mistaken for a missing lock table.
    private static bool IsMissingTable(Exception ex, string tableName)
    {
        var msg = ex.Message;
        if (string.IsNullOrEmpty(msg) || !msg.Contains(tableName, StringComparison.OrdinalIgnoreCase)) return false;
        return msg.Contains("no such table", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase);
    }

    private void LogUnavailableOnce(Exception? ex, string reason)
    {
        if (Interlocked.Exchange(ref _missingTableWarned, 1) == 0)
        {
            _logger?.LogWarning(ex,
                "OrionGuard outbox lock unavailable: {Reason}. Distributed locking, and so outbox dispatch, is disabled until then. " +
                "Single-instance consumers who do not want the lock table should call opts.UseDistributedLock<NullDistributedLock>().",
                reason);
        }
    }

    /// <summary>
    /// The lock's SQL for one model. Identifiers come from the <see cref="OutboxLock"/> mapping (table, schema and
    /// column names, including any naming convention) and are delimited by the provider's
    /// <see cref="ISqlGenerationHelper"/>, so PostgreSQL does not fold them to lower case. Values stay parameters:
    /// <c>{0}</c>..<c>{3}</c> are EF Core raw-SQL placeholders.
    /// </summary>
    internal sealed record LockStatements(string TableName, string Acquire, string Insert, string Release)
    {
        /// <summary>Builds the statements, or returns <see langword="null"/> when <see cref="OutboxLock"/> is not mapped to a table.</summary>
        public static LockStatements? For(DbContext db)
        {
            var entityType = db.Model.FindEntityType(typeof(OutboxLock));
            var tableName = entityType?.GetTableName();
            if (entityType is null || tableName is null)
            {
                return null;
            }

            var schema = entityType.GetSchema();
            var store = StoreObjectIdentifier.Table(tableName, schema);
            var sql = db.GetService<ISqlGenerationHelper>();
            string Column(string property) => sql.DelimitIdentifier(entityType.FindProperty(property)!.GetColumnName(store)!);

            var table = sql.DelimitIdentifier(tableName, schema);
            var key = Column(nameof(OutboxLock.LockKey));
            var holder = Column(nameof(OutboxLock.HolderId));
            var acquired = Column(nameof(OutboxLock.AcquiredOnUtc));
            var expires = Column(nameof(OutboxLock.ExpiresOnUtc));

            return new LockStatements(
                tableName,
                // {0} holder, {1} now, {2} expires, {3} key
                $"UPDATE {table} SET {holder} = {{0}}, {acquired} = {{1}}, {expires} = {{2}} " +
                $"WHERE {key} = {{3}} AND ({holder} IS NULL OR {expires} <= {{1}})",
                // {0} key, {1} holder, {2} now, {3} expires
                $"INSERT INTO {table} ({key}, {holder}, {acquired}, {expires}) SELECT {{0}}, {{1}}, {{2}}, {{3}} " +
                $"WHERE NOT EXISTS (SELECT 1 FROM {table} WHERE {key} = {{0}})",
                // {0} now, {1} key, {2} holder
                $"UPDATE {table} SET {holder} = NULL, {expires} = {{0}} WHERE {key} = {{1}} AND {holder} = {{2}}");
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
