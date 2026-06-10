namespace Moongazing.OrionGuard.Outbox.Dashboard;

/// <summary>
/// Read-only projection of a failed / poisoned outbox row returned by the dashboard's
/// listing endpoint. Mirrors the safe subset of the
/// <c>EntityFrameworkCore.Outbox.OutboxMessage</c> fields - payload is deliberately
/// excluded so the default listing cannot leak event bodies to an under-authorized viewer.
/// </summary>
/// <param name="Id">Stable row id.</param>
/// <param name="EventType">Assembly-qualified .NET type name of the event.</param>
/// <param name="OccurredOnUtc">Original event timestamp.</param>
/// <param name="RetryCount">Number of dispatch attempts so far.</param>
/// <param name="Error">Truncated error text (see <see cref="OutboxDashboardOptions.ErrorTruncationLength"/>).</param>
/// <param name="CorrelationId">Correlation id captured at write time, when present.</param>
public sealed record OutboxFailedMessageRow(
    Guid Id,
    string EventType,
    DateTime OccurredOnUtc,
    int RetryCount,
    string? Error,
    string? CorrelationId);
