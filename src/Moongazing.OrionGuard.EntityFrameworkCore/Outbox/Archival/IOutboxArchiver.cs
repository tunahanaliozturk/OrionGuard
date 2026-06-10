namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Pluggable archival strategy invoked by <see cref="OutboxArchivalHostedService"/> once
/// per batch. Default <see cref="DeleteOutboxArchiver"/> deletes rows past the retention
/// cutoff; consumers wire a custom strategy (copy to archive table, push to object storage,
/// etc.) by registering their own <see cref="IOutboxArchiver"/> before
/// <c>opts.UseOutboxArchival(...)</c>.
/// </summary>
public interface IOutboxArchiver
{
    /// <summary>
    /// Archive at most <see cref="OutboxArchivalOptions.BatchSize"/> processed rows whose
    /// <see cref="OutboxMessage.ProcessedOnUtc"/> is older than <paramref name="cutoff"/>.
    /// The implementation MUST honour <see cref="OutboxArchivalOptions.PreserveDeadLetters"/>
    /// when filtering rows. Returns the number of rows archived this batch (used by the
    /// hosted service for logging / metrics).
    /// </summary>
    /// <param name="dbContext">The consumer's <see cref="DbContext"/>; resolved fresh per batch.</param>
    /// <param name="cutoff">UTC instant before which rows are eligible for archival.</param>
    /// <param name="options">Snapshot of the archival options for this batch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> ArchiveAsync(
        DbContext dbContext,
        DateTime cutoff,
        OutboxArchivalOptions options,
        CancellationToken cancellationToken);
}
