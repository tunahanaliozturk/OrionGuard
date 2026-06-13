namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

using System.Diagnostics.Metrics;

/// <summary>
/// v6.5.16 OpenTelemetry instrumentation for the outbox dispatcher. Exposes
/// <see cref="QueueLag"/> so operators can graph p99 dispatch latency
/// (<c>OccurredOnUtc</c> -> <c>ProcessedOnUtc</c>) and spot dispatcher backpressure
/// before the queue depth gauge moves.
/// </summary>
public static class OutboxDispatcherDiagnostics
{
    /// <summary>Meter name used by the dispatcher.</summary>
    public const string MeterName = "Moongazing.OrionGuard.Outbox.Dispatcher";

    private static readonly Meter Meter = new(MeterName, "6.5.16");

    /// <summary>
    /// Per-row dispatch lag: <c>now - OccurredOnUtc</c> at the moment the dispatcher
    /// successfully dispatches AND persists the row. Dead-letter paths emit their own
    /// signals and are intentionally excluded here so the histogram tail reflects only
    /// successful dispatch latency. Operators graph p50/p99 to spot dispatcher slowdown
    /// before rows pile up beyond the steady-state <c>orionguard.outbox.dispatched_count</c> rate.
    /// </summary>
    internal static readonly Histogram<double> QueueLag = Meter.CreateHistogram<double>(
        "orionguard.outbox.dispatcher.queue_lag", unit: "ms",
        description: "Per-row dispatch lag (OccurredOnUtc -> ProcessedOnUtc).");

    /// <summary>
    /// Record one row's dispatch lag in milliseconds. Negative values (capture host
    /// clock skewed past dispatch host) are clamped to 0 so they do not pull the
    /// histogram p50 down.
    /// </summary>
    public static void RecordQueueLag(double milliseconds)
        => QueueLag.Record(System.Math.Max(0d, milliseconds));

    /// <summary>
    /// v6.5.17 idle-poll counter. Increments each time <c>ProcessBatchAsync</c> finds
    /// an empty backlog. Operators graph the rate vs the total poll rate to answer
    /// "is the dispatcher running too often for the actual traffic? raise PollingInterval".
    /// A high idle-poll fraction is a cost-of-poll signal; a low fraction means the
    /// dispatcher is busy and BatchSize / parallelism may need raising instead.
    /// </summary>
    internal static readonly Counter<long> IdlePolls = Meter.CreateCounter<long>(
        "orionguard.outbox.dispatcher.poll.idle", unit: "{polls}",
        description: "Dispatcher cycles that found an empty backlog.");

    /// <summary>Record one idle poll. Public so consumer-owned dispatchers can opt in.</summary>
    public static void RecordIdlePoll() => IdlePolls.Add(1);

    /// <summary>
    /// v6.5.18 per-row dispatch failure counter. Increments for EVERY swallowed exception
    /// in the dispatcher's row loop (transient + terminal). Distinct from the dead-letter
    /// path which only fires when RetryCount >= MaxRetries; this counter exposes the full
    /// failure surface so operators see the upstream pressure that precedes a dead-letter.
    /// </summary>
    internal static readonly Counter<long> DispatchErrors = Meter.CreateCounter<long>(
        "orionguard.outbox.dispatcher.errors", unit: "{errors}",
        description: "Per-row dispatch failures swallowed by the dispatcher (transient + terminal).");

    /// <summary>Record a per-row dispatch failure tagged with the exception type.</summary>
    public static void RecordDispatchError(string exceptionType)
        => DispatchErrors.Add(1, new System.Collections.Generic.KeyValuePair<string, object?>("exception_type", exceptionType));

    /// <summary>
    /// v6.5.22 distribution of OutboxMessage rows added per SaveChangesAsync invocation
    /// on the producer side. Operators graph p99 to spot a SaveChanges call that emits
    /// an unusually large batch (often a sign of bulk-imports or accidental fan-out
    /// from aggregate domain events). Zero-row saves do NOT emit (every SaveChanges
    /// would otherwise pollute the histogram with 0 samples).
    /// </summary>
    internal static readonly Histogram<int> EnqueuedRowsPerSave = Meter.CreateHistogram<int>(
        "orionguard.outbox.enqueued_rows_per_save", unit: "{rows}",
        description: "Outbox rows added by the DomainEventSaveChangesInterceptor per SaveChanges (non-zero only).");

    /// <summary>Record one non-empty SaveChanges enqueue batch. Public for consumer-owned producers.</summary>
    public static void RecordEnqueuedRowsPerSave(int rowCount)
    {
        if (rowCount <= 0)
        {
            return;
        }
        EnqueuedRowsPerSave.Record(rowCount);
    }

    /// <summary>
    /// v6.5.19 distribution of dispatched row payload sizes in bytes (the JSON Payload
    /// column length). Operators graph p99 to size storage column types, connection-
    /// pool buffers, and spot tenant bulk-import paths whose payloads grew suddenly.
    /// Recorded only on the successful dispatch path.
    /// </summary>
    internal static readonly Histogram<int> RowPayloadSizeBytes = Meter.CreateHistogram<int>(
        "orionguard.outbox.dispatcher.row_size_bytes", unit: "By",
        description: "Per-row dispatched payload size in bytes.");

    /// <summary>Record one successfully-dispatched row's payload size in bytes.</summary>
    public static void RecordRowPayloadSize(int bytes)
    {
        if (bytes <= 0)
        {
            return;
        }
        RowPayloadSizeBytes.Record(bytes);
    }

    /// <summary>
    /// v6.5.24 per-row <c>IDomainEventDispatcher.DispatchAsync</c> wall-clock. Operators
    /// graph p99 to isolate the consumer's downstream dispatch cost from the v6.5.16
    /// queue_lag (which sums queue time + dispatch + commit) and from the v6.5.21
    /// archival cycle duration. ALL outcomes emit (try/finally) so a slow timing-out
    /// dispatcher surfaces even on the failure path.
    /// </summary>
    internal static readonly Histogram<double> DispatchDurationMs = Meter.CreateHistogram<double>(
        "orionguard.outbox.dispatcher.dispatch_duration_ms", unit: "ms",
        description: "Per-row IDomainEventDispatcher.DispatchAsync wall-clock (success + failure).");

    /// <summary>Record one DispatchAsync call's wall-clock. Negatives are clamped to 0.</summary>
    public static void RecordDispatchDuration(double milliseconds)
        => DispatchDurationMs.Record(System.Math.Max(0d, milliseconds));

    /// <summary>
    /// v6.5.25 distribution of rows claimed per dispatcher poll cycle. Operators graph
    /// p99 to spot a dispatcher consistently maxing out <c>BatchSize</c> (raise the
    /// batch / parallelism) or staying near zero (over-sized polling cadence).
    /// Zero-row cycles do NOT emit; idle polling is the v6.5.17 idle-poll counter's job.
    /// Mirrors v0.7.18 Audit and v0.2.16 Patch batch_size shapes on the Guard side.
    /// </summary>
    internal static readonly Histogram<int> DispatcherBatchSize = Meter.CreateHistogram<int>(
        "orionguard.outbox.dispatcher.batch_size", unit: "{rows}",
        description: "Outbox rows claimed per dispatcher poll cycle (non-empty cycles only).");

    /// <summary>Record one non-empty poll cycle's row count. Public for consumer-owned dispatchers.</summary>
    public static void RecordDispatcherBatchSize(int rowCount)
    {
        if (rowCount <= 0)
        {
            return;
        }
        DispatcherBatchSize.Record(rowCount);
    }
}
