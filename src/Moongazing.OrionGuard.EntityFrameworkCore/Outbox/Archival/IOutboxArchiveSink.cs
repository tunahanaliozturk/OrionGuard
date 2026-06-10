namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

/// <summary>
/// Abstraction for the destination an archival batch lands in. Plug a consumer-owned
/// implementation in DI (S3, Azure Blob, GCS, local filesystem) and the
/// <see cref="BlobOutboxArchiver"/> calls <see cref="WriteAsync"/> with a freshly-built
/// stream per batch. The sink is responsible for picking the actual storage key under
/// the supplied key hint (e.g. prefix with a date partition, append an extension).
/// </summary>
/// <remarks>
/// The contract is intentionally narrow: a single write call. Errors propagate, and the
/// archiver treats any thrown exception as a failed batch (rows are NOT deleted; the
/// sweep retries next tick). Implementations MUST flush all bytes before returning so
/// the archiver can safely delete the source rows on the live table.
/// </remarks>
public interface IOutboxArchiveSink
{
    /// <summary>
    /// Persist <paramref name="payload"/> bytes (typically newline-delimited JSON
    /// snapshots of the archived rows) under a storage key derived from
    /// <paramref name="keyHint"/>. Implementations may decorate the hint with timestamps,
    /// partition prefixes, or content suffixes; the caller does NOT depend on the final
    /// key shape.
    /// </summary>
    /// <param name="keyHint">Stable suggestion for the key (e.g. <c>"outbox-2026-06-10T12-00-00Z"</c>).</param>
    /// <param name="payload">Bytes to write; ownership stays with the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteAsync(string keyHint, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}
