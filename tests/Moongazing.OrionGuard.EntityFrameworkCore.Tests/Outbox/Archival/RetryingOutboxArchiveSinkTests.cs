namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Archival;

using System.Text;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Xunit;

public sealed class RetryingOutboxArchiveSinkTests
{
    private sealed class ScriptedSink : IOutboxArchiveSink
    {
        private readonly Queue<Func<Task>> script;
        public int Calls { get; private set; }
        public ScriptedSink(params Func<Task>[] actions) => script = new Queue<Func<Task>>(actions);
        public Task WriteAsync(string keyHint, ReadOnlyMemory<byte> payload, CancellationToken ct)
        {
            Calls++;
            return script.Count > 0 ? script.Dequeue()() : Task.CompletedTask;
        }
    }

    private static RetryingOutboxArchiveSinkOptions FastRetry(int maxAttempts = 5) => new()
    {
        MaxAttempts = maxAttempts,
        BaseDelay = TimeSpan.FromMilliseconds(1),
        MaxDelay = TimeSpan.FromMilliseconds(5),
    };

    [Fact]
    public async Task Succeeds_immediately_when_inner_does_not_throw()
    {
        var inner = new ScriptedSink();
        var sut = new RetryingOutboxArchiveSink(inner, FastRetry());

        await sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), CancellationToken.None);

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Retries_until_inner_succeeds()
    {
        var failuresLeft = 2;
        var inner = new ScriptedSink(
            () => failuresLeft-- > 0 ? Task.FromException(new InvalidOperationException("transient")) : Task.CompletedTask,
            () => failuresLeft-- > 0 ? Task.FromException(new InvalidOperationException("transient")) : Task.CompletedTask,
            () => Task.CompletedTask);
        var sut = new RetryingOutboxArchiveSink(inner, FastRetry());

        await sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), CancellationToken.None);

        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Rethrows_last_exception_after_MaxAttempts()
    {
        var inner = new ScriptedSink(
            () => Task.FromException(new InvalidOperationException("attempt-1")),
            () => Task.FromException(new InvalidOperationException("attempt-2")),
            () => Task.FromException(new InvalidOperationException("attempt-3-final")));
        var sut = new RetryingOutboxArchiveSink(inner, FastRetry(maxAttempts: 3));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), CancellationToken.None));
        Assert.Equal("attempt-3-final", ex.Message);
        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task IsRetryable_predicate_short_circuits_non_transient_failures()
    {
        var inner = new ScriptedSink(
            () => Task.FromException(new ArgumentException("invalid payload")));
        var sut = new RetryingOutboxArchiveSink(inner, new RetryingOutboxArchiveSinkOptions
        {
            MaxAttempts = 5,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(5),
            IsRetryable = ex => ex is not ArgumentException,
        });

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), CancellationToken.None));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Cancellation_propagates_without_consuming_retries()
    {
        using var cts = new CancellationTokenSource();
        var inner = new ScriptedSink(
            () =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });
        var sut = new RetryingOutboxArchiveSink(inner, FastRetry());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.WriteAsync("k", Encoding.UTF8.GetBytes("p"), cts.Token));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public void Options_validate_at_construction()
    {
        var inner = new ScriptedSink();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryingOutboxArchiveSink(inner, new RetryingOutboxArchiveSinkOptions { MaxAttempts = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RetryingOutboxArchiveSink(inner, new RetryingOutboxArchiveSinkOptions { BaseDelay = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() =>
            new RetryingOutboxArchiveSink(inner, new RetryingOutboxArchiveSinkOptions
            {
                BaseDelay = TimeSpan.FromMilliseconds(100),
                MaxDelay = TimeSpan.FromMilliseconds(50),
            }));
    }
}
