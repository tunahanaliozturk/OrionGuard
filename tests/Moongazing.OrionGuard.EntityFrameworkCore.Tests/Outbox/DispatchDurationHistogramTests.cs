namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

using System.Diagnostics.Metrics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Xunit;

public sealed class DispatchDurationHistogramTests
{
    [Fact]
    public void RecordDispatchDuration_emits_for_positive_ms()
    {
        var samples = new System.Collections.Generic.List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.dispatch_duration_ms")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxDispatcherDiagnostics.RecordDispatchDuration(75.5);

        lock (samples) { Assert.Contains(75.5, samples); }
    }

    [Fact]
    public void RecordDispatchDuration_clamps_negative_to_zero()
    {
        var samples = new System.Collections.Generic.List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.dispatch_duration_ms")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxDispatcherDiagnostics.RecordDispatchDuration(-30.0);

        lock (samples) { Assert.Contains(0.0, samples); }
    }
}
