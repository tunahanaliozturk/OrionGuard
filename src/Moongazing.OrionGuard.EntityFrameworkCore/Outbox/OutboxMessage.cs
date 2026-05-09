namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>
/// Persisted representation of a domain event awaiting dispatch. Written to the consumer's DbContext
/// in the same transaction as aggregate state changes; consumed by <c>OutboxDispatcherHostedService</c>.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Unique row id (auto-assigned via <see cref="Guid.NewGuid"/>).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Assembly-qualified .NET type name of the serialized event.</summary>
    public string EventType { get; set; } = default!;

    /// <summary>System.Text.Json payload of the event.</summary>
    public string Payload { get; set; } = default!;

    /// <summary>UTC timestamp on the originating event.</summary>
    public DateTime OccurredOnUtc { get; set; }

    /// <summary>Set when the worker successfully dispatched (or dead-lettered) the row.</summary>
    public DateTime? ProcessedOnUtc { get; set; }

    /// <summary>Most-recent dispatch error (if any).</summary>
    public string? Error { get; set; }

    /// <summary>Number of dispatch attempts so far.</summary>
    public int RetryCount { get; set; }

    /// <summary>Optional correlation id for tracing.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>W3C trace context parent id captured when the row was written.</summary>
    public string? TraceParent { get; set; }

    /// <summary>W3C trace state captured when the row was written.</summary>
    public string? TraceState { get; set; }
}
