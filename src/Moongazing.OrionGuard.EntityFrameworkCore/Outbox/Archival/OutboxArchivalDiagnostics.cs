namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

using System.Diagnostics.Metrics;

/// <summary>
/// v6.5.13 OpenTelemetry instrumentation for the outbox archival pipeline. Currently
/// exposes <see cref="ArchiveBytesWritten"/> so operators can graph archival volume on a
/// per-sink basis (Grafana panel: <c>rate(orionguard_outbox_archive_bytes_written_total[5m])</c>)
/// alongside the existing <c>orionguard.outbox.archived_rows</c> counter.
/// </summary>
public static class OutboxArchivalDiagnostics
{
    /// <summary>Meter name used by the archival sinks.</summary>
    public const string MeterName = "Moongazing.OrionGuard.Outbox.Archival";

    private static readonly Meter Meter = new(MeterName, "6.5.13");

    /// <summary>
    /// Bytes written to an archive sink per <see cref="IOutboxArchiveSink.WriteAsync"/>
    /// call, tagged with <c>sink</c> (a short identifier each sink supplies). Use this to
    /// monitor archive throughput and storage growth without scraping S3 / blob listings.
    /// </summary>
    internal static readonly Counter<long> ArchiveBytesWritten = Meter.CreateCounter<long>(
        "orionguard.outbox.archive.bytes_written");

    /// <summary>
    /// Record a successful sink write. Sinks call this immediately after their inner
    /// flush returns. Non-positive payloads are ignored.
    /// </summary>
    public static void RecordBytes(long bytes, string sinkName)
    {
        ArgumentException.ThrowIfNullOrEmpty(sinkName);
        if (bytes <= 0)
        {
            return;
        }
        ArchiveBytesWritten.Add(bytes, new System.Collections.Generic.KeyValuePair<string, object?>("sink", sinkName));
    }

    /// <summary>
    /// v6.5.20 distribution of rows archived per <see cref="OutboxArchivalHostedService.ArchiveBatchAsync"/>
    /// call. Operators graph p99 to spot a backend that is consistently maxing out the
    /// archival batch size (sign that throughput needs raising) or staying near zero (sign
    /// that the polling cadence is over-sized). Zero-row cycles do NOT emit; idle archival
    /// is tracked by the existing OutboxArchivalState liveness gauge.
    /// </summary>
    internal static readonly Histogram<int> ArchiveBatchSize = Meter.CreateHistogram<int>(
        "orionguard.outbox.archival.batch_size", unit: "{rows}",
        description: "Rows archived per archival cycle (non-empty cycles only).");

    /// <summary>Record one non-empty archival cycle's row count. Public for consumer-owned archivers.</summary>
    public static void RecordArchiveBatchSize(int rowCount)
    {
        if (rowCount <= 0)
        {
            return;
        }
        ArchiveBatchSize.Record(rowCount);
    }

    /// <summary>
    /// v6.5.21 wall-clock duration of one archival cycle in milliseconds. Operators
    /// graph p99 to spot a backend whose archive write throughput has regressed
    /// independently of the row count (e.g. a slow blob sink keeps the dispatcher
    /// honest but hurts throughput).
    /// </summary>
    internal static readonly Histogram<double> ArchiveCycleDuration = Meter.CreateHistogram<double>(
        "orionguard.outbox.archival.duration_ms", unit: "ms",
        description: "Wall-clock duration of one archival cycle (all cycles, including zero-row).");

    /// <summary>Record an archival cycle's wall-clock. ALL cycles emit including zero-row.</summary>
    public static void RecordArchiveCycleDuration(double milliseconds)
        => ArchiveCycleDuration.Record(System.Math.Max(0d, milliseconds));

    /// <summary>
    /// v6.5.26 archival failure counter. Increments when an archival batch throws and
    /// is swallowed by the worker's catch block (transient backend faults, sink errors).
    /// Operators alert on the rate to catch a stuck archival pipeline that the v6.5.14
    /// liveness gauge alone cannot distinguish from a healthy-but-idle worker. Tagged
    /// with <c>exception_type</c>.
    /// </summary>
    internal static readonly Counter<long> ArchiveFailures = Meter.CreateCounter<long>(
        "orionguard.outbox.archival.failures", unit: "{failures}",
        description: "Archival batches that threw and were swallowed by the worker (transient + terminal).");

    /// <summary>Record one swallowed archival failure tagged with the exception type.</summary>
    public static void RecordArchiveFailure(string exceptionType)
        => ArchiveFailures.Add(1, new System.Collections.Generic.KeyValuePair<string, object?>("exception_type", exceptionType));
}
