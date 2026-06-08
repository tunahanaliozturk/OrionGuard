namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;

/// <summary>
/// Default <see cref="IOutboxWakeSignal"/> registration. Polling-only: <see cref="WaitForNextTickAsync"/>
/// simply awaits the supplied polling interval, and <see cref="SignalAsync"/> is a no-op.
/// Behaviour is byte-for-byte identical to the v6.4 / v6.5.0 dispatcher loop, so consumers
/// who do not opt into push-based dispatch see no functional change.
/// </summary>
public sealed class NullOutboxWakeSignal : IOutboxWakeSignal
{
    /// <inheritdoc />
    public Task WaitForNextTickAsync(TimeSpan pollingInterval, CancellationToken cancellationToken) =>
        Task.Delay(pollingInterval, cancellationToken);

    /// <inheritdoc />
    public ValueTask SignalAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
