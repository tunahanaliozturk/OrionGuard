namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Default <see cref="IOutboxArchiver"/>: deletes rows past the retention cutoff via
/// <see cref="EntityFrameworkQueryableExtensions.ExecuteDeleteAsync{TSource}(IQueryable{TSource}, CancellationToken)"/>.
/// Honours <see cref="OutboxArchivalOptions.PreserveDeadLetters"/>; orders by
/// <see cref="OutboxMessage.ProcessedOnUtc"/> so the oldest rows leave first.
/// </summary>
public sealed class DeleteOutboxArchiver : IOutboxArchiver
{
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

        return await query
            .OrderBy(m => m.ProcessedOnUtc)
            .Take(options.BatchSize)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
