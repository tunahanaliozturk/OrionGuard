namespace Moongazing.OrionGuard.Domain.Rules;

/// <summary>
/// A synchronous domain business rule. In v6.1.0 only the interface ships so that
/// <c>Entity.CheckRule</c> can enforce invariants; the <c>BusinessRule</c> abstract base class,
/// <c>Guard.Against.BrokenRule</c>, and <c>Validate.Rule</c> helpers arrive in v6.3.0.
/// </summary>
public interface IBusinessRule
{
    /// <summary>Returns <see langword="true"/> if this rule is currently violated.</summary>
    bool IsBroken();

    /// <summary>Localization key for the rule's message (looked up in <c>ValidationMessages</c>).</summary>
    string MessageKey { get; }

    /// <summary>Fallback message used when no translation is registered for <see cref="MessageKey"/>.</summary>
    string DefaultMessage { get; }

    /// <summary>Optional format arguments for the localized message.</summary>
    object[]? MessageArgs => null;
}
