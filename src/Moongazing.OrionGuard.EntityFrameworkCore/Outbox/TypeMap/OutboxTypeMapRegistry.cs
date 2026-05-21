using System.Diagnostics.CodeAnalysis;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;

/// <summary>
/// Maps stable logical names (e.g. <c>"user.registered"</c>) to CLR event types. Used by the outbox
/// dispatcher to avoid <see cref="Type.GetType(string)"/> reflection and to decouple persisted
/// payloads from internal type identity. Populated once at startup; no thread-safety on <c>Map</c>.
/// </summary>
public sealed class OutboxTypeMapRegistry
{
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _byType = new();

    /// <summary>Maps an event type to a logical name. Throws on collision (same name to different type, or vice versa).</summary>
    public OutboxTypeMapRegistry Map<TEvent>(string logicalName) where TEvent : IDomainEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);

        var type = typeof(TEvent);

        if (_byName.TryGetValue(logicalName, out var existingType) && existingType != type)
        {
            throw new InvalidOperationException(
                $"Outbox type map collision: '{logicalName}' is already mapped to {existingType.FullName}.");
        }
        if (_byType.TryGetValue(type, out var existingName) && existingName != logicalName)
        {
            throw new InvalidOperationException(
                $"Outbox type map collision: {type.FullName} is already mapped to '{existingName}'.");
        }

        _byName[logicalName] = type;
        _byType[type] = logicalName;
        return this;
    }

    /// <summary>Attempts to resolve a logical name to its registered CLR type.</summary>
    public bool TryResolve(string logicalName, [NotNullWhen(true)] out Type? type)
        => _byName.TryGetValue(logicalName, out type);

    /// <summary>Attempts to resolve a CLR type to its registered logical name.</summary>
    public bool TryGetLogicalName(Type type, [NotNullWhen(true)] out string? logicalName)
        => _byType.TryGetValue(type, out logicalName);
}
