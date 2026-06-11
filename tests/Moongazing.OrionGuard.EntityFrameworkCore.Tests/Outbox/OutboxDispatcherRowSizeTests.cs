namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

using System.Diagnostics.Metrics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Xunit;

public sealed class OutboxDispatcherRowSizeTests
{
    [Fact]
    public void RecordRowPayloadSize_emits_for_positive_bytes()
    {
        var samples = new System.Collections.Generic.List<int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.row_size_bytes")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxDispatcherDiagnostics.RecordRowPayloadSize(1024);

        lock (samples)
        {
            Assert.Contains(1024, samples);
        }
    }

    [Fact]
    public void RecordRowPayloadSize_ignores_zero_and_negative_bytes()
    {
        var samples = new System.Collections.Generic.List<int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.row_size_bytes")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxDispatcherDiagnostics.RecordRowPayloadSize(0);
        OutboxDispatcherDiagnostics.RecordRowPayloadSize(-5);

        lock (samples)
        {
            Assert.DoesNotContain(0, samples);
            Assert.DoesNotContain(-5, samples);
        }
    }
}
