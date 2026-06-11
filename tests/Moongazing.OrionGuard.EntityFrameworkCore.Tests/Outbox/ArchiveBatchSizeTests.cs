namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

using System.Diagnostics.Metrics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Xunit;

public sealed class ArchiveBatchSizeTests
{
    [Fact]
    public void RecordArchiveBatchSize_emits_for_positive_count()
    {
        var samples = new System.Collections.Generic.List<int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxArchivalDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.archival.batch_size")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxArchivalDiagnostics.RecordArchiveBatchSize(250);

        lock (samples) { Assert.Contains(250, samples); }
    }

    [Fact]
    public void RecordArchiveBatchSize_ignores_zero_and_negative_input()
    {
        var samples = new System.Collections.Generic.List<int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxArchivalDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.archival.batch_size")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxArchivalDiagnostics.RecordArchiveBatchSize(0);
        OutboxArchivalDiagnostics.RecordArchiveBatchSize(-7);

        lock (samples)
        {
            Assert.DoesNotContain(0, samples);
            Assert.DoesNotContain(-7, samples);
        }
    }
}
