namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;

/// <summary>Controls fallback behaviour when an outbox row's <c>EventType</c> is not in the registry.</summary>
public sealed class OutboxTypeMapOptions
{
    /// <summary>
    /// When true, the dispatcher falls back to <see cref="Type.GetType(string)"/> for event types not
    /// registered in the <see cref="OutboxTypeMapRegistry"/>. Default <see langword="true"/>, because rows
    /// written without a type map store assembly-qualified names and would otherwise be dead-lettered.
    /// </summary>
    /// <remarks>
    /// Security trade-off: with the fallback on, the event type comes from the row, so anyone who can write to
    /// the outbox table can have any loadable <c>IDomainEvent</c> type deserialized from a payload they choose
    /// and dispatched to its handlers. Types that do not implement <c>IDomainEvent</c> are rejected before
    /// deserialization. The dispatcher logs a warning the first time it resolves a row through the fallback.
    /// To turn it off, map every event type with <c>UseOutboxTypeMap</c>, let the rows written before the
    /// mapping drain, then set this to <see langword="false"/>. Set it to <see langword="false"/> for AOT-only
    /// deployments too.
    /// </remarks>
    public bool AllowAssemblyQualifiedNameFallback { get; set; } = true;
}
