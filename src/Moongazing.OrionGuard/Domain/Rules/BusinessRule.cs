namespace Moongazing.OrionGuard.Domain.Rules;

/// <summary>
/// Abstract base for synchronous business rules. Subclasses implement <see cref="IsBroken"/> and
/// <see cref="DefaultMessage"/>; <see cref="MessageKey"/> defaults to the CLR type name.
/// </summary>
public abstract class BusinessRule : IBusinessRule
{
    /// <inheritdoc />
    public abstract bool IsBroken();

    /// <inheritdoc />
    public abstract string DefaultMessage { get; }

    /// <inheritdoc />
    public virtual string MessageKey => GetType().Name;

    /// <inheritdoc />
    public virtual object[]? MessageArgs => null;
}
