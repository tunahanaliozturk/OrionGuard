namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;

using System.Diagnostics.Metrics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Xunit;

public sealed class ArchiveBytesWrittenTests : IDisposable
{
    private readonly string tempDir;

    public ArchiveBytesWrittenTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "orionguard-bytes-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFile_sink_records_payload_bytes_on_write()
    {
        long total = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxArchivalDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.archive.bytes_written")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, val, tags, _) =>
        {
            foreach (var t in tags)
            {
                if (t.Key == "sink" && t.Value is string s && s == "local-file")
                {
                    Interlocked.Add(ref total, val);
                }
            }
        });
        listener.Start();

        var sink = new LocalFileOutboxArchiveSink(tempDir);
        var payload = System.Text.Encoding.UTF8.GetBytes("abcdef");
        await sink.WriteAsync("k", payload, CancellationToken.None);

        Assert.Equal(6, Interlocked.Read(ref total));
    }

    [Fact]
    public async Task RotatingFile_sink_records_payload_bytes_on_write()
    {
        long total = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxArchivalDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.archive.bytes_written")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, val, tags, _) =>
        {
            foreach (var t in tags)
            {
                if (t.Key == "sink" && t.Value is string s && s == "rotating-file")
                {
                    Interlocked.Add(ref total, val);
                }
            }
        });
        listener.Start();

        var sink = new RotatingFileOutboxArchiveSink(new RotatingFileOutboxArchiveSinkOptions
        {
            RootDirectory = tempDir,
            MaxFileBytes = 1024,
        });
        var payload = System.Text.Encoding.UTF8.GetBytes("hello-rotating");
        await sink.WriteAsync("k", payload, CancellationToken.None);

        Assert.Equal(payload.Length, Interlocked.Read(ref total));
    }

    [Fact]
    public void RecordBytes_ignores_non_positive_payload_sizes()
    {
        OutboxArchivalDiagnostics.RecordBytes(0, "noop");
        OutboxArchivalDiagnostics.RecordBytes(-5, "noop");
    }
}
