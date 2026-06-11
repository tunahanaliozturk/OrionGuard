namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

using System.Diagnostics.Metrics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Xunit;

public sealed class OutboxDispatcherIdlePollTests
{
    [Fact]
    public void RecordIdlePoll_emits_a_single_increment()
    {
        long count = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.poll.idle")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, val, _, _) => Interlocked.Add(ref count, val));
        listener.Start();

        OutboxDispatcherDiagnostics.RecordIdlePoll();

        Assert.Equal(1, Interlocked.Read(ref count));
    }
}
