namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// v6.5.23 consumer-supplied observer invoked when the dispatcher swallows a per-row
/// failure. Mirror of v0.2.18 Patch <c>IDeadLetterSink</c> on the Guard producer side.
/// Useful for routing failures to triage / alerting systems (Slack, PagerDuty,
/// follow-up review queue) without tangling that logic into the load-bearing dispatch
/// transaction.
/// </summary>
/// <remarks>
/// <para>
/// The observer fires for EVERY swallowed failure (transient + terminal), pairing with
/// the v6.5.18 <c>dispatcher.errors</c> counter shape. A throwing observer does NOT
/// affect the database state (the row's RetryCount/Error fields are already updated by
/// the dispatcher); observer exceptions are caught and logged at warning level.
/// </para>
/// <para>
/// No observer is registered by default. Consumers wire one via
/// <c>services.AddSingleton&lt;IOutboxRowFailureObserver, MyObserver&gt;()</c>.
/// </para>
/// </remarks>
public interface IOutboxRowFailureObserver
{
    /// <summary>Notify the observer that a dispatcher row failed.</summary>
    /// <param name="rowId">Outbox row id.</param>
    /// <param name="eventType">Logical event type id resolved from the registry / AQN.</param>
    /// <param name="attempt">Attempt number that just failed (1 = first try).</param>
    /// <param name="isTerminal">True when this failure pushed RetryCount >= MaxRetries.</param>
    /// <param name="exception">The exception the dispatcher caught.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task OnRowFailedAsync(
        Guid rowId,
        string eventType,
        int attempt,
        bool isTerminal,
        Exception exception,
        CancellationToken cancellationToken);
}

/// <summary>Default no-op observer used when no consumer-registered observer is present.</summary>
public sealed class NullOutboxRowFailureObserver : IOutboxRowFailureObserver
{
    /// <inheritdoc />
    public Task OnRowFailedAsync(
        Guid rowId,
        string eventType,
        int attempt,
        bool isTerminal,
        Exception exception,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
