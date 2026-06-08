namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Push;

using System.Diagnostics;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;
using Xunit;

public sealed class OutboxWakeSignalTests
{
    [Fact]
    public async Task NullSignal_waits_polling_interval_then_returns()
    {
        IOutboxWakeSignal signal = new NullOutboxWakeSignal();
        var sw = Stopwatch.StartNew();

        await signal.WaitForNextTickAsync(TimeSpan.FromMilliseconds(150), CancellationToken.None);

        sw.Stop();
        Assert.InRange(sw.ElapsedMilliseconds, 100, 1000);
    }

    [Fact]
    public async Task NullSignal_signal_is_a_no_op()
    {
        IOutboxWakeSignal signal = new NullOutboxWakeSignal();

        // Calling Signal does not throw and returns synchronously. Subsequent WaitForNextTick
        // still observes the full polling interval (no buffered wake).
        await signal.SignalAsync(CancellationToken.None);
        var sw = Stopwatch.StartNew();
        await signal.WaitForNextTickAsync(TimeSpan.FromMilliseconds(150), CancellationToken.None);
        sw.Stop();

        Assert.InRange(sw.ElapsedMilliseconds, 100, 1000);
    }

    [Fact]
    public async Task ChannelSignal_pending_signal_wakes_immediately()
    {
        IOutboxWakeSignal signal = new ChannelOutboxWakeSignal();
        await signal.SignalAsync(CancellationToken.None);

        var sw = Stopwatch.StartNew();
        await signal.WaitForNextTickAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        sw.Stop();

        // A signal already in the channel should unblock the wait in well under 100ms,
        // never waiting for the 30s polling interval.
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"expected fast wake but waited {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task ChannelSignal_falls_back_to_polling_after_interval()
    {
        IOutboxWakeSignal signal = new ChannelOutboxWakeSignal();

        var sw = Stopwatch.StartNew();
        await signal.WaitForNextTickAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None);
        sw.Stop();

        Assert.InRange(sw.ElapsedMilliseconds, 150, 2000);
    }

    [Fact]
    public async Task ChannelSignal_signal_arriving_mid_wait_wakes_dispatcher()
    {
        IOutboxWakeSignal signal = new ChannelOutboxWakeSignal();

        var waitTask = signal.WaitForNextTickAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        // Signal after a short delay. The wait should complete well before 30s.
        await Task.Delay(100);
        await signal.SignalAsync(CancellationToken.None);

        var sw = Stopwatch.StartNew();
        await waitTask;
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"expected fast wake but waited {sw.ElapsedMilliseconds} ms after signal");
    }

    [Fact]
    public async Task ChannelSignal_repeated_signals_coalesce_to_one_wake()
    {
        IOutboxWakeSignal signal = new ChannelOutboxWakeSignal();

        for (int i = 0; i < 10; i++)
        {
            await signal.SignalAsync(CancellationToken.None);
        }

        await signal.WaitForNextTickAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        // After consuming the buffered wake, the next wait should fall back to polling.
        var sw = Stopwatch.StartNew();
        await signal.WaitForNextTickAsync(TimeSpan.FromMilliseconds(150), CancellationToken.None);
        sw.Stop();
        Assert.InRange(sw.ElapsedMilliseconds, 100, 2000);
    }

    [Fact]
    public async Task ChannelSignal_external_cancellation_propagates()
    {
        IOutboxWakeSignal signal = new ChannelOutboxWakeSignal();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            signal.WaitForNextTickAsync(TimeSpan.FromSeconds(30), cts.Token));
    }
}
