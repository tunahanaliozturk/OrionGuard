using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Moongazing.OrionGuard.OpenTelemetry.DomainEvents;

/// <summary>
/// Activity source, meter, and per-instrument constants for OrionGuard domain-event telemetry.
/// Consumers register them with OpenTelemetry via <c>WithOpenTelemetryDomainEvents()</c> (Task 15).
/// </summary>
public static class OrionGuardDomainEventTelemetry
{
    /// <summary>The ActivitySource name registered for domain-event spans.</summary>
    public const string ActivitySourceName = "Moongazing.OrionGuard.DomainEvents";

    /// <summary>The Meter name registered for domain-event metrics.</summary>
    public const string MeterName = "Moongazing.OrionGuard.DomainEvents";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, "6.3.0");
    internal static readonly Meter Meter = new(MeterName, "6.3.0");

    internal static readonly Counter<long> EventsDispatched = Meter.CreateCounter<long>(
        "orionguard.domain_events.dispatched", unit: "events", description: "Total domain events dispatched");

    internal static readonly Counter<long> EventsFailed = Meter.CreateCounter<long>(
        "orionguard.domain_events.failed", unit: "events", description: "Failed domain event dispatches");

    internal static readonly Histogram<double> DispatchDuration = Meter.CreateHistogram<double>(
        "orionguard.domain_events.duration", unit: "ms", description: "Dispatch duration in milliseconds");

    internal static readonly Counter<long> OutboxProcessed = Meter.CreateCounter<long>(
        "orionguard.outbox.processed", unit: "messages", description: "Outbox messages processed");

    internal static readonly Counter<long> OutboxRetries = Meter.CreateCounter<long>(
        "orionguard.outbox.retries", unit: "retries", description: "Outbox retry attempts");
}
