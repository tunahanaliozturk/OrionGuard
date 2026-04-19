using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moongazing.OrionGuard.Domain.Exceptions;
using Moongazing.OrionGuard.Domain.Rules;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Base class for Domain-Driven Design entities — objects whose identity persists across state
/// changes. Equality is determined by <see cref="Id"/>, not by the values of other properties.
/// </summary>
/// <typeparam name="TId">The type of the entity identifier. Must be a non-null type.</typeparam>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    /// <summary>Gets the entity identifier.</summary>
    public TId Id { get; protected set; } = default!;

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity{TId}"/> class with the given identifier.
    /// </summary>
    /// <param name="id">The entity identifier. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null.</exception>
    protected Entity(TId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Entity{TId}"/> class without an identifier.
    /// This constructor is provided for Entity Framework Core and serializer scenarios.
    /// </summary>
    protected Entity() { }

    /// <summary>
    /// Determines whether the specified entity is equal to the current entity by comparing their identifiers.
    /// </summary>
    /// <param name="other">The entity to compare with the current entity.</param>
    /// <returns>
    /// <see langword="true"/> if the current entity and <paramref name="other"/> have the same identifier
    /// and are the same type; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Entity<TId> e && Equals(e);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        EqualityComparer<TId>.Default.GetHashCode(Id!);

    /// <summary>Determines whether two entities are equal by comparing their identifiers.</summary>
    /// <param name="left">The first entity to compare.</param>
    /// <param name="right">The second entity to compare.</param>
    /// <returns>
    /// <see langword="true"/> if both entities are null or have the same identifier and type;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
        => left is null ? right is null : left.Equals(right);

    /// <summary>Determines whether two entities are not equal by comparing their identifiers.</summary>
    /// <param name="left">The first entity to compare.</param>
    /// <param name="right">The second entity to compare.</param>
    /// <returns>
    /// <see langword="true"/> if the entities are not equal; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);

    /// <summary>
    /// Enforces a synchronous business rule. Throws <see cref="BusinessRuleValidationException"/>
    /// if the rule reports itself as broken.
    /// </summary>
    /// <param name="rule">The business rule to validate. Cannot be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
    /// <exception cref="BusinessRuleValidationException">Thrown when the rule is broken.</exception>
    protected static void CheckRule(IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.IsBroken())
        {
            throw new BusinessRuleValidationException(rule);
        }
    }

    /// <summary>
    /// Enforces an asynchronous business rule. Throws <see cref="BusinessRuleValidationException"/>
    /// if the rule reports itself as broken.
    /// </summary>
    /// <param name="rule">The asynchronous business rule to validate. Cannot be null.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the validation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
    /// <exception cref="BusinessRuleValidationException">Thrown when the rule is broken.</exception>
    protected static async Task CheckRuleAsync(IAsyncBusinessRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (await rule.IsBrokenAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessRuleValidationException(rule);
        }
    }
}
