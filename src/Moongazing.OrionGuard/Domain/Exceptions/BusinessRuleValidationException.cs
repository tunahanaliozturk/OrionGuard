using System;
using Moongazing.OrionGuard.Domain.Rules;
using Moongazing.OrionGuard.Localization;

namespace Moongazing.OrionGuard.Domain.Exceptions;

/// <summary>
/// Thrown when a synchronous or asynchronous <see cref="IBusinessRule"/> / <see cref="IAsyncBusinessRule"/>
/// reports that it is broken.
/// </summary>
public sealed class BusinessRuleValidationException : Exception
{
    /// <summary>
    /// Gets the localization key for this rule's validation message.
    /// </summary>
    public string MessageKey { get; }

    /// <summary>
    /// Gets the name of the business rule that was broken.
    /// </summary>
    public string RuleName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BusinessRuleValidationException"/> class with a synchronous business rule.
    /// </summary>
    /// <param name="rule">The business rule that was broken.</param>
    public BusinessRuleValidationException(IBusinessRule rule)
        : base(Resolve(rule.MessageKey, rule.DefaultMessage, rule.MessageArgs))
    {
        MessageKey = rule.MessageKey;
        RuleName = rule.GetType().Name;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BusinessRuleValidationException"/> class with an asynchronous business rule.
    /// </summary>
    /// <param name="rule">The asynchronous business rule that was broken.</param>
    public BusinessRuleValidationException(IAsyncBusinessRule rule)
        : base(Resolve(rule.MessageKey, rule.DefaultMessage, rule.MessageArgs))
    {
        MessageKey = rule.MessageKey;
        RuleName = rule.GetType().Name;
    }

    private static string Resolve(string key, string fallback, object[]? args)
    {
        var localized = args is null
            ? ValidationMessages.Get(key)
            : ValidationMessages.Get(key, args);

        // ValidationMessages.Get returns the key itself when no translation is registered.
        return string.Equals(localized, key, StringComparison.Ordinal) ? fallback : localized;
    }
}
