namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

using System.Diagnostics.Metrics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Xunit;

public sealed class ArchiveCycleDurationTests
{
    [Fact]
    public void RecordArchiveCycleDuration_emits_for_positive_milliseconds()
    {
        var samples = new System.Collections.Generic.List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxArchivalDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.archival.duration_ms")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxArchivalDiagnostics.RecordArchiveCycleDuration(125.5);

        lock (samples) { Assert.Contains(125.5, samples); }
    }

    [Fact]
    public void RecordArchiveCycleDuration_clamps_negative_input_to_zero()
    {
        var samples = new System.Collections.Generic.List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxArchivalDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.archival.duration_ms")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxArchivalDiagnostics.RecordArchiveCycleDuration(-50.0);

        lock (samples) { Assert.Contains(0.0, samples); }
    }
}
