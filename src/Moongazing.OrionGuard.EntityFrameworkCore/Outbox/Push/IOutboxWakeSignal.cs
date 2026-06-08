namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;

/// <summary>
/// Optional push-based wake-up contract for the outbox dispatcher. Concrete implementations
/// (Postgres LISTEN/NOTIFY, SQL Server Service Broker, Redis PubSub, etc.) replace the default
/// polling-only wait with an event-driven wake so newly enqueued rows are dispatched without
/// waiting for the next polling tick.
/// </summary>
/// <remarks>
/// The default registration is <see cref="NullOutboxWakeSignal"/>, which preserves the v6.4 /
/// v6.5.0 polling-only behaviour. Consumers opt in to push-based dispatch by registering a
/// concrete <see cref="IOutboxWakeSignal"/> in DI. The dispatcher always honours the polling
/// interval as an upper bound on wake latency, so a misbehaving signal (missed NOTIFY,
/// dropped connection) cannot stall dispatch indefinitely.
/// </remarks>
public interface IOutboxWakeSignal
{
    /// <summary>
    /// Wait until either <paramref name="pollingInterval"/> elapses or a wake signal arrives,
    /// whichever happens first. Returns when work may be available.
    /// </summary>
    /// <param name="pollingInterval">Upper bound on the wait. Acts as the polling-fallback period.</param>
    /// <param name="cancellationToken">Cancellation token observed during the wait.</param>
    Task WaitForNextTickAsync(TimeSpan pollingInterval, CancellationToken cancellationToken);

    /// <summary>
    /// Signal that new outbox rows are available. Should wake any pending
    /// <see cref="WaitForNextTickAsync"/> call. Called by enqueue paths and by concrete push
    /// backends (a Postgres LISTEN handler, for instance).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SignalAsync(CancellationToken cancellationToken);
}
