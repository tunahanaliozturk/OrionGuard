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
}
