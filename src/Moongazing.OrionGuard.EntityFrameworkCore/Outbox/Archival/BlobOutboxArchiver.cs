namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

using System.Buffers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// <see cref="IOutboxArchiver"/> that ships expiring outbox rows to an external blob
/// store (S3, Azure Blob, GCS, local filesystem) via an <see cref="IOutboxArchiveSink"/>
/// before deleting them from the live table. Pairs with the v6.5.6
/// <see cref="CopyToTableOutboxArchiver{TArchiveRow}"/> archive-table pattern but ships
/// off-box: consumers wire a cloud-object-store sink and expiring rows leave the database
/// entirely after archival.
/// </summary>
/// <remarks>
/// <para>
/// Write order matters: the sink call runs FIRST, then the delete. A sink failure aborts
/// the sweep (the rows stay on the live table for the next tick). The delete is gated on
/// the same eligibility predicate the SELECT used so a concurrent replay-endpoint cannot
/// lose its intent between the snapshot and the delete (matches the v6.5.6 copy-then-delete
/// safety pattern).
/// </para>
/// <para>
/// Payload format: newline-delimited JSON (<c>.jsonl</c>) records, one per row. Each
/// record carries the fields the BlobOutboxArchiver decided to ship; consumers wanting a
/// different shape register a different archiver. The default record shape is a
/// projection over id, event type, payload, occurred/processed timestamps, retry count,
/// error, and correlation id.
/// </para>
/// </remarks>
public sealed class BlobOutboxArchiver : IOutboxArchiver
{
    private readonly IOutboxArchiveSink sink;
    private readonly Func<DateTime> nowUtc;

    /// <summary>Construct with a sink. The <c>nowUtc</c> hook lets tests stamp a deterministic key hint.</summary>
    public BlobOutboxArchiver(IOutboxArchiveSink sink, Func<DateTime>? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        this.sink = sink;
        this.nowUtc = nowUtc ?? (() => DateTime.UtcNow);
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

        var preserveDeadLetters = options.PreserveDeadLetters;
        var rows = await dbContext.Set<OutboxMessage>()
            .AsNoTracking()
            .Where(m => m.ProcessedOnUtc != null
                     && m.ProcessedOnUtc < cutoff
                     && (!preserveDeadLetters || m.Error == null))
            .OrderBy(m => m.ProcessedOnUtc)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return 0;
        }

        // Serialise to JSON Lines before the sink call so a serialisation failure also
        // aborts the sweep without partial state.
        var payload = SerializeJsonLines(rows);
        var keyHint = $"outbox-{nowUtc().ToString("yyyy-MM-ddTHH-mm-ssZ", System.Globalization.CultureInfo.InvariantCulture)}";
        await sink.WriteAsync(keyHint, payload, cancellationToken).ConfigureAwait(false);

        // Re-check eligibility against the live state before deleting; matches the
        // v6.5.6 CopyToTable safety pattern.
        var ids = rows.Select(r => r.Id).ToList();
        return await dbContext.Set<OutboxMessage>()
            .Where(m => ids.Contains(m.Id)
                     && m.ProcessedOnUtc != null
                     && m.ProcessedOnUtc < cutoff
                     && (!preserveDeadLetters || m.Error == null))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static byte[] SerializeJsonLines(IReadOnlyList<OutboxMessage> rows)
    {
        var buffer = new ArrayBufferWriter<byte>(initialCapacity: 4096);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                writer.WriteString("id", row.Id);
                writer.WriteString("eventType", row.EventType);
                writer.WriteString("payload", row.Payload);
                writer.WriteString("occurredOnUtc", row.OccurredOnUtc);
                if (row.ProcessedOnUtc.HasValue)
                {
                    writer.WriteString("processedOnUtc", row.ProcessedOnUtc.Value);
                }
                writer.WriteNumber("retryCount", row.RetryCount);
                if (row.Error is not null)
                {
                    writer.WriteString("error", row.Error);
                }
                if (row.CorrelationId is not null)
                {
                    writer.WriteString("correlationId", row.CorrelationId);
                }
                writer.WriteEndObject();
                writer.Flush();
                buffer.Write(System.Text.Encoding.UTF8.GetBytes(Environment.NewLine));
                writer.Reset();
            }
        }
        return buffer.WrittenSpan.ToArray();
    }
}
