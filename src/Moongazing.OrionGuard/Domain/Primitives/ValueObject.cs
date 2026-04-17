using System;
using System.Collections.Generic;
using System.Linq;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Base class for Domain-Driven Design value objects whose equality is determined by the values
/// of their components, not by reference identity.
/// </summary>
public abstract class ValueObject : IValueObject, IEquatable<ValueObject>
{
    /// <summary>
    /// Enumerates the components that participate in equality comparison. Override to yield the
    /// values that constitute the identity of this value object.
    /// </summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => obj is ValueObject vo && Equals(vo);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
