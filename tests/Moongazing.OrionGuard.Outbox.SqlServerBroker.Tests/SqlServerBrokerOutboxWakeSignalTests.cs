namespace Moongazing.OrionGuard.Outbox.SqlServerBroker.Tests;

using System.Diagnostics;
using Microsoft.Extensions.Options;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;

/// <summary>
/// In-process contract tests. Real WAITFOR-against-Service-Broker integration tests run
/// against a live SQL Server instance in a separate test project; the WAITFOR semantics
/// require a database, so this csproj sticks to the in-memory channel contract.
/// </summary>
public sealed class SqlServerBrokerOutboxWakeSignalTests
{
    private static SqlServerBrokerOutboxWakeSignal NewSignal() => new(
        Options.Create(new SqlServerBrokerOptions { ConnectionString = "ignored-for-unit-tests" }));

    [Fact]
    public async Task SignalAsync_arriving_before_wait_completes_immediately()
    {
        IOutboxWakeSignal signal = NewSignal();
        await signal.SignalAsync(CancellationToken.None);

        var sw = Stopwatch.StartNew();
        await signal.WaitForNextTickAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000, $"expected fast wake; took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task WaitForNextTickAsync_polling_interval_elapses_without_signal()
    {
        IOutboxWakeSignal signal = NewSignal();

        var sw = Stopwatch.StartNew();
        await signal.WaitForNextTickAsync(TimeSpan.FromMilliseconds(200), CancellationToken.None);
        sw.Stop();

        Assert.InRange(sw.ElapsedMilliseconds, 150, 2000);
    }

    [Fact]
    public async Task SignalAsync_arriving_mid_wait_wakes_dispatcher()
    {
        IOutboxWakeSignal signal = NewSignal();

        var sw = Stopwatch.StartNew();
        var waitTask = signal.WaitForNextTickAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        await Task.Delay(100);
        await signal.SignalAsync(CancellationToken.None);

        await waitTask;
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 2000, $"expected fast mid-wait wake; took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Repeated_signals_coalesce_into_one_wake()
    {
        IOutboxWakeSignal signal = NewSignal();

        for (var i = 0; i < 10; i++)
        {
            await signal.SignalAsync(CancellationToken.None);
        }

        await signal.WaitForNextTickAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        var sw = Stopwatch.StartNew();
        await signal.WaitForNextTickAsync(TimeSpan.FromMilliseconds(150), CancellationToken.None);
        sw.Stop();

        Assert.InRange(sw.ElapsedMilliseconds, 100, 2000);
    }

    [Fact]
    public async Task External_cancellation_throws_OperationCanceledException()
    {
        IOutboxWakeSignal signal = NewSignal();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => signal.WaitForNextTickAsync(TimeSpan.FromSeconds(30), cts.Token));
    }
}
