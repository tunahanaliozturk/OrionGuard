namespace Moongazing.OrionGuard.Domain.Rules;

/// <summary>
/// Abstract base for asynchronous business rules. Subclasses implement <see cref="IsBrokenAsync"/>
/// and <see cref="DefaultMessage"/>; <see cref="MessageKey"/> defaults to the CLR type name.
/// </summary>
public abstract class AsyncBusinessRule : IAsyncBusinessRule
{
    /// <inheritdoc />
    public abstract Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract string DefaultMessage { get; }

    /// <inheritdoc />
    public virtual string MessageKey => GetType().Name;

    /// <inheritdoc />
    public virtual object[]? MessageArgs => null;
}
