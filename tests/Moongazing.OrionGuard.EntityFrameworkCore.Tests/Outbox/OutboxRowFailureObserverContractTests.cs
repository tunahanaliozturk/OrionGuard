namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox;

using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Xunit;

public sealed class OutboxRowFailureObserverContractTests
{
    [Fact]
    public async Task NullOutboxRowFailureObserver_OnRowFailedAsync_completes_without_throwing()
    {
        var sut = new NullOutboxRowFailureObserver();

        await sut.OnRowFailedAsync(
            System.Guid.NewGuid(),
            "Demo.Event",
            attempt: 1,
            isTerminal: false,
            new System.InvalidOperationException("boom"),
            System.Threading.CancellationToken.None);
    }

    [Fact]
    public async Task Custom_observer_receives_rowId_event_type_attempt_terminal_and_exception()
    {
        System.Guid? capturedId = null;
        string? capturedEventType = null;
        int capturedAttempt = -1;
        bool? capturedTerminal = null;
        System.Exception? capturedEx = null;

        var sut = new CapturingObserver((id, type, attempt, terminal, ex) =>
        {
            capturedId = id;
            capturedEventType = type;
            capturedAttempt = attempt;
            capturedTerminal = terminal;
            capturedEx = ex;
        });

        var rowId = System.Guid.NewGuid();
        var ex = new System.TimeoutException("downstream timed out");

        await sut.OnRowFailedAsync(rowId, "Demo.Order.Placed", attempt: 4, isTerminal: true, ex, System.Threading.CancellationToken.None);

        Assert.Equal(rowId, capturedId);
        Assert.Equal("Demo.Order.Placed", capturedEventType);
        Assert.Equal(4, capturedAttempt);
        Assert.True(capturedTerminal);
        Assert.Same(ex, capturedEx);
    }

    private sealed class CapturingObserver : IOutboxRowFailureObserver
    {
        private readonly System.Action<System.Guid, string, int, bool, System.Exception> capture;
        public CapturingObserver(System.Action<System.Guid, string, int, bool, System.Exception> capture) => this.capture = capture;
        public Task OnRowFailedAsync(System.Guid rowId, string eventType, int attempt, bool isTerminal, System.Exception exception, System.Threading.CancellationToken cancellationToken)
        {
            capture(rowId, eventType, attempt, isTerminal, exception);
            return Task.CompletedTask;
        }
    }
}
