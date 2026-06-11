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
}
