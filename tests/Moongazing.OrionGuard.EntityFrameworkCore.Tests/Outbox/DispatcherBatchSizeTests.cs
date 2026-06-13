namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

using System.Diagnostics.Metrics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Xunit;

public sealed class DispatcherBatchSizeTests
{
    [Fact]
    public void RecordDispatcherBatchSize_emits_for_positive_count()
    {
        var samples = new System.Collections.Generic.List<int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.batch_size")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxDispatcherDiagnostics.RecordDispatcherBatchSize(42);

        lock (samples) { Assert.Contains(42, samples); }
    }

    [Fact]
    public void RecordDispatcherBatchSize_ignores_zero_and_negative_input()
    {
        var samples = new System.Collections.Generic.List<int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.batch_size")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxDispatcherDiagnostics.RecordDispatcherBatchSize(0);
        OutboxDispatcherDiagnostics.RecordDispatcherBatchSize(-7);

        lock (samples)
        {
            Assert.DoesNotContain(0, samples);
            Assert.DoesNotContain(-7, samples);
        }
    }
}
