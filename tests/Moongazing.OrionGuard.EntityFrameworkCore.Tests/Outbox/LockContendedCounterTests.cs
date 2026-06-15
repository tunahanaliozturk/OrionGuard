namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

using System.Diagnostics.Metrics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Xunit;

public sealed class LockContendedCounterTests
{
    [Fact]
    public void RecordLockContended_increments_the_counter()
    {
        long total = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OutboxDispatcherDiagnostics.MeterName
                && instrument.Name == "orionguard.outbox.dispatcher.lock_contended")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, val, _, _) =>
            System.Threading.Interlocked.Add(ref total, val));
        listener.Start();

        OutboxDispatcherDiagnostics.RecordLockContended();
        OutboxDispatcherDiagnostics.RecordLockContended();

        Assert.Equal(2L, System.Threading.Interlocked.Read(ref total));
    }
}
