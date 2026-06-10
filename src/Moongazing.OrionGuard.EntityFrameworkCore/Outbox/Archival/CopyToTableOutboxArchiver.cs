namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// <see cref="IOutboxArchiver"/> that COPIES rows past the retention cutoff into a
/// consumer-supplied archive table, then deletes the originals in the SAME transaction so
/// no row is lost across the boundary. Useful for compliance regimes that require retaining
/// the audit trail of dispatched events past the live-table retention window.
/// </summary>
/// <typeparam name="TArchiveRow">
/// Consumer-owned entity that maps to the archive table. The mapping function passed to
/// the constructor projects a live <see cref="OutboxMessage"/> into a
/// <typeparamref name="TArchiveRow"/>.
/// </typeparam>
public sealed class CopyToTableOutboxArchiver<TArchiveRow> : IOutboxArchiver
    where TArchiveRow : class
{
    private readonly Func<OutboxMessage, TArchiveRow> map;

    /// <summary>Construct with the projection function applied to each row before delete.</summary>
    public CopyToTableOutboxArchiver(Func<OutboxMessage, TArchiveRow> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        this.map = map;
    }

    /// <inheritdoc />
    public async Task<int> ArchiveAsync(
        DbContext dbContext,
        DateTime cutoff,
        OutboxArchivalOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);

        var query = dbContext.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc < cutoff);

        if (options.PreserveDeadLetters)
        {
            query = query.Where(m => m.Error == null);
        }

        // Snapshot the eligible rows BEFORE we mutate the table. AsNoTracking keeps the
        // change-tracker clean; the Take honours the batch size so we do not OOM a
        // long-running consumer.
        var live = await query
            .OrderBy(m => m.ProcessedOnUtc)
            .Take(options.BatchSize)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (live.Count == 0)
        {
            return 0;
        }

        // Copy + delete in ONE transaction. The archive insert and the live delete must
        // commit atomically or we risk losing rows (delete without insert) or duplicating
        // them (insert without delete).
        await using var tx = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var archiveRows = live.Select(map).ToList();
        await dbContext.Set<TArchiveRow>().AddRangeAsync(archiveRows, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var ids = live.Select(m => m.Id).ToList();
        var deleted = await dbContext.Set<OutboxMessage>()
            .Where(m => ids.Contains(m.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }
}
