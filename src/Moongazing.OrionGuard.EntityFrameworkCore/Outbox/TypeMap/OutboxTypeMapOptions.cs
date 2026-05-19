namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;

/// <summary>Controls fallback behaviour when an outbox row's <c>EventType</c> is not in the registry.</summary>
public sealed class OutboxTypeMapOptions
{
    /// <summary>
    /// When true, the dispatcher falls back to <see cref="Type.GetType(string)"/> for event types not
    /// registered in the <see cref="OutboxTypeMapRegistry"/>. Default <see langword="true"/> for v6.3 source compatibility.
    /// Set false for AOT-only deployments.
    /// </summary>
    public bool AllowAssemblyQualifiedNameFallback { get; set; } = true;
}
