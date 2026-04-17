namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Marker interface for value objects in the Domain-Driven Design sense.
/// <para>
/// Apply to <see langword="record"/> types to get structural equality for free, or inherit from
/// <see cref="ValueObject"/> for behavior-rich value objects with explicit equality components.
/// </para>
/// </summary>
public interface IValueObject
{
}
