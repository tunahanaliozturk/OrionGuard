namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;

using System.Text;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Xunit;

public sealed class CompositeOutboxArchiveSinkTests
{
    private sealed class RecordingSink : IOutboxArchiveSink
    {
        public int Calls { get; private set; }
        public Func<Task>? Behaviour { get; set; }
        public Task WriteAsync(string keyHint, ReadOnlyMemory<byte> payload, CancellationToken ct)
        {
            Calls++;
            return Behaviour?.Invoke() ?? Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FailFast_forwards_to_every_sink_in_order_on_success()
    {
        var a = new RecordingSink();
        var b = new RecordingSink();
        var c = new RecordingSink();
        var sut = new CompositeOutboxArchiveSink(new[] { a, b, c });

        await sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), CancellationToken.None);

        Assert.Equal(1, a.Calls);
        Assert.Equal(1, b.Calls);
        Assert.Equal(1, c.Calls);
    }

    [Fact]
    public async Task FailFast_aborts_on_first_failure_and_does_not_call_remaining_sinks()
    {
        var a = new RecordingSink();
        var b = new RecordingSink { Behaviour = () => throw new InvalidOperationException("boom") };
        var c = new RecordingSink();
        var sut = new CompositeOutboxArchiveSink(new[] { a, b, c });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), CancellationToken.None));

        Assert.Equal(1, a.Calls);
        Assert.Equal(1, b.Calls);
        Assert.Equal(0, c.Calls);
    }

    [Fact]
    public async Task BestEffort_calls_every_sink_even_after_a_failure()
    {
        var a = new RecordingSink();
        var b = new RecordingSink { Behaviour = () => throw new InvalidOperationException("boom") };
        var c = new RecordingSink();
        var sut = new CompositeOutboxArchiveSink(new[] { a, b, c }, CompositeOutboxArchiveSinkMode.BestEffort);

        await sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), CancellationToken.None);

        Assert.Equal(1, a.Calls);
        Assert.Equal(1, b.Calls);
        Assert.Equal(1, c.Calls);
    }

    [Fact]
    public async Task BestEffort_throws_AggregateException_when_every_sink_fails()
    {
        var a = new RecordingSink { Behaviour = () => throw new InvalidOperationException("a") };
        var b = new RecordingSink { Behaviour = () => throw new InvalidOperationException("b") };
        var sut = new CompositeOutboxArchiveSink(new[] { a, b }, CompositeOutboxArchiveSinkMode.BestEffort);

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), CancellationToken.None));
        Assert.Equal(2, ex.InnerExceptions.Count);
    }

    [Fact]
    public async Task BestEffort_returns_success_when_at_least_one_sink_succeeds()
    {
        var a = new RecordingSink { Behaviour = () => throw new InvalidOperationException("boom") };
        var b = new RecordingSink();
        var sut = new CompositeOutboxArchiveSink(new[] { a, b }, CompositeOutboxArchiveSinkMode.BestEffort);

        await sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), CancellationToken.None);

        Assert.Equal(1, a.Calls);
        Assert.Equal(1, b.Calls);
    }

    [Fact]
    public async Task Cancellation_propagates_in_both_modes_without_consuming_remaining_sinks()
    {
        using var cts = new CancellationTokenSource();
        var a = new RecordingSink
        {
            Behaviour = () => { cts.Cancel(); throw new OperationCanceledException(cts.Token); },
        };
        var b = new RecordingSink();
        var sut = new CompositeOutboxArchiveSink(new[] { a, b }, CompositeOutboxArchiveSinkMode.BestEffort);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), cts.Token));
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Constructor_rejects_empty_sink_list()
    {
        Assert.Throws<ArgumentException>(() =>
            new CompositeOutboxArchiveSink(Array.Empty<IOutboxArchiveSink>()));
    }

    [Fact]
    public void Constructor_rejects_null_sink_in_list()
    {
        Assert.Throws<ArgumentException>(() =>
            new CompositeOutboxArchiveSink(new IOutboxArchiveSink?[] { null }!));
    }
}
