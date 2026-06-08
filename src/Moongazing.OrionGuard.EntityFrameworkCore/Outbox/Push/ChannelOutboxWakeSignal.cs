namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;

using System.Threading.Channels;

/// <summary>
/// In-process <see cref="IOutboxWakeSignal"/> backed by a bounded
/// <see cref="System.Threading.Channels.Channel{T}"/>. Useful for unit tests and for the
/// "enqueue inside the same process as the dispatcher" pattern, where the
/// <c>SaveChangesInterceptor</c> can publish a wake signal directly. Distributed (cross-process)
/// push backends ship in v6.5.2 (Postgres LISTEN/NOTIFY) and v6.5.3 (SQL Server Service Broker).
/// </summary>
/// <remarks>
/// Wake signals coalesce: repeated calls to <see cref="SignalAsync"/> while the dispatcher
/// is in <see cref="WaitForNextTickAsync"/> all unblock the same single wait. A signal that
/// arrives while the dispatcher is mid-batch is buffered and consumed by the next wait.
/// </remarks>
public sealed class ChannelOutboxWakeSignal : IOutboxWakeSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(capacity: 1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <inheritdoc />
    public async Task WaitForNextTickAsync(TimeSpan pollingInterval, CancellationToken cancellationToken)
    {
        using var pollingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pollingCts.CancelAfter(pollingInterval);

        try
        {
            await channel.Reader.ReadAsync(pollingCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (pollingCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Polling interval elapsed without a signal — return normally so the dispatcher
            // runs a polling tick.
        }
    }

    /// <inheritdoc />
    public ValueTask SignalAsync(CancellationToken cancellationToken)
    {
        // DropWrite policy: if a signal is already pending, this call is a no-op.
        _ = channel.Writer.TryWrite(true);
        return ValueTask.CompletedTask;
    }
}
