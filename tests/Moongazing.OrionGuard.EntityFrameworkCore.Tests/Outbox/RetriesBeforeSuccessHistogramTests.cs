namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

using System.Diagnostics.Metrics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Xunit;

public sealed class RetriesBeforeSuccessHistogramTests
{
    private const string InstrumentName = "orionguard.outbox.dispatcher.retries_before_success";

    private static System.Collections.Generic.List<int> Capture(System.Action act)
    {
        var samples = new System.Collections.Generic.List<int>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == InstrumentName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, val, _, _) =>
        {
            lock (samples) { samples.Add(val); }
        });
        listener.Start();

        act();

        lock (samples) { return new System.Collections.Generic.List<int>(samples); }
    }

    [Fact]
    public void RecordRetriesBeforeSuccess_emits_a_first_try_zero()
    {
        // A first-try success (RetryCount 0) IS recorded: the fraction of zeros is the signal.
        var samples = Capture(() => OutboxDispatcherDiagnostics.RecordRetriesBeforeSuccess(0));
        Assert.Contains(0, samples);
    }

    [Fact]
    public void RecordRetriesBeforeSuccess_emits_the_retry_count()
    {
        var samples = Capture(() => OutboxDispatcherDiagnostics.RecordRetriesBeforeSuccess(3));
        Assert.Contains(3, samples);
    }

    [Fact]
    public void RecordRetriesBeforeSuccess_clamps_negative_to_zero()
    {
        var samples = Capture(() => OutboxDispatcherDiagnostics.RecordRetriesBeforeSuccess(-2));
        Assert.Contains(0, samples);
        Assert.DoesNotContain(-2, samples);
    }
}
