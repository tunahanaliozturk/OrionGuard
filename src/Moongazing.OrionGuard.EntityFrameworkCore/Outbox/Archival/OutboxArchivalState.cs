namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

/// <summary>
/// v6.5.14 shared singleton that records when the
/// <see cref="OutboxArchivalHostedService"/> last completed a batch successfully. The
/// <see cref="OutboxArchivalHealthCheck"/> consults this state to decide whether the
/// background worker is alive (recent) or stuck (stale). The hosted service is the only
/// writer; the health check is the only reader.
/// </summary>
public sealed class OutboxArchivalState
{
    private long lastSuccessfulBatchUtcTicks;
    private int totalBatches;

    /// <summary>UTC timestamp of the most recent successful <c>ArchiveBatchAsync</c> call, or null if none yet.</summary>
    public DateTime? LastSuccessfulBatchUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref lastSuccessfulBatchUtcTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>Number of successful batches observed since service start. Monotonic.</summary>
    public int TotalBatches => Interlocked.CompareExchange(ref totalBatches, 0, 0);

    /// <summary>Called by the hosted service after each successful batch. Idempotent on the timestamp.</summary>
    public void RecordSuccessfulBatch(DateTime utcNow)
    {
        Interlocked.Exchange(ref lastSuccessfulBatchUtcTicks, utcNow.Ticks);
        Interlocked.Increment(ref totalBatches);
    }
}
