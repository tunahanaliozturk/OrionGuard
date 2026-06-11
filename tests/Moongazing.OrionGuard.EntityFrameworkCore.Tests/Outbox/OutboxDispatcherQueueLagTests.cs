namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

using System.Diagnostics.Metrics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Xunit;

public sealed class OutboxDispatcherQueueLagTests
{
    [Fact]
    public void RecordQueueLag_emits_a_non_negative_sample()
    {
        var samples = new System.Collections.Generic.List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.queue_lag")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        OutboxDispatcherDiagnostics.RecordQueueLag(125.5);

        lock (samples)
        {
            Assert.Contains(125.5, samples);
        }
    }

    [Fact]
    public void RecordQueueLag_clamps_negative_values_to_zero()
    {
        var samples = new System.Collections.Generic.List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.queue_lag")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        // Clock skew between capture host and dispatcher host would surface here. The
        // contract is: skewed-negative -> recorded as 0 so the histogram p50 stays
        // meaningful.
        OutboxDispatcherDiagnostics.RecordQueueLag(-50);

        lock (samples)
        {
            Assert.Contains(0d, samples);
            Assert.DoesNotContain(-50d, samples);
        }
    }
}
