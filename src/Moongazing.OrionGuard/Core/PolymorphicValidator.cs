namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Validates objects based on their runtime type using type-specific validation rules.
/// Supports discriminated unions and interface-based polymorphism.
/// </summary>
public sealed class PolymorphicValidator<TBase> where TBase : class
{
    private readonly Dictionary<Type, Func<TBase, GuardResult>> _validators = new();
    private Func<TBase, GuardResult>? _fallback;

    /// <summary>
    /// Register validation rules for a specific derived type.
    /// Usage: .When&lt;CreditCardPayment&gt;(p => Validate.For(p).Property(...).ToResult())
    /// </summary>
    public PolymorphicValidator<TBase> When<TDerived>(Func<TDerived, GuardResult> validator) where TDerived : TBase
    {
        _validators[typeof(TDerived)] = instance => validator((TDerived)instance);
        return this;
    }

    /// <summary>
    /// Register a fallback validator for unregistered types.
    /// </summary>
    public PolymorphicValidator<TBase> Otherwise(Func<TBase, GuardResult> fallback)
    {
        _fallback = fallback;
        return this;
    }

    /// <summary>
    /// Validates the instance using the registered type-specific validator.
    /// </summary>
    public GuardResult Validate(TBase instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var actualType = instance.GetType();

        if (_validators.TryGetValue(actualType, out var validator))
            return validator(instance);

        // Check base types and interfaces
        foreach (var (type, v) in _validators)
        {
            if (type.IsAssignableFrom(actualType))
                return v(instance);
        }

        if (_fallback is not null)
            return _fallback(instance);

        return GuardResult.Success(); // No validator registered — pass
    }

    /// <summary>
    /// Validates and throws if invalid.
    /// </summary>
    public TBase ValidateAndThrow(TBase instance)
    {
        Validate(instance).ThrowIfInvalid();
        return instance;
    }
}
