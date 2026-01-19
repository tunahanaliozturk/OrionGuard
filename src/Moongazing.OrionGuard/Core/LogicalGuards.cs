namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Provides a way to chain multiple validations with OR logic.
/// If any validation passes, the chain succeeds.
/// </summary>
public sealed class OrGuard<T>
{
    private readonly T _value;
    private readonly string _parameterName;
    private readonly List<Func<T, bool>> _predicates;
    private readonly List<string> _messages;
    private bool _anyPassed;

    internal OrGuard(T value, string parameterName)
    {
        _value = value;
        _parameterName = parameterName;
        _predicates = new List<Func<T, bool>>();
        _messages = new List<string>();
        _anyPassed = false;
    }

    /// <summary>
    /// Adds a validation that the value must satisfy (OR logic).
    /// </summary>
    public OrGuard<T> Or(Func<T, bool> predicate, string message)
    {
        _predicates.Add(predicate);
        _messages.Add(message);
        if (predicate(_value))
        {
            _anyPassed = true;
        }
        return this;
    }

    /// <summary>
    /// Validates that at least one condition passed.
    /// </summary>
    public T Validate()
    {
        if (!_anyPassed && _predicates.Count > 0)
        {
            throw new AggregateValidationException(new[]
            {
                new ValidationError(_parameterName, $"{_parameterName} must satisfy at least one of: {string.Join(" OR ", _messages)}")
            });
        }
        return _value;
    }

    /// <summary>
    /// Returns a result instead of throwing.
    /// </summary>
    public GuardResult ToResult()
    {
        if (!_anyPassed && _predicates.Count > 0)
        {
            return GuardResult.Failure(_parameterName, $"{_parameterName} must satisfy at least one of: {string.Join(" OR ", _messages)}");
        }
        return GuardResult.Success();
    }
}

/// <summary>
/// Provides a way to chain multiple validations with AND logic with short-circuit evaluation.
/// </summary>
public sealed class AndGuard<T>
{
    private readonly T _value;
    private readonly string _parameterName;
    private readonly List<ValidationError> _errors;
    private bool _shortCircuit;

    internal AndGuard(T value, string parameterName, bool shortCircuit = false)
    {
        _value = value;
        _parameterName = parameterName;
        _errors = new List<ValidationError>();
        _shortCircuit = shortCircuit;
    }

    /// <summary>
    /// Adds a validation that must pass (AND logic).
    /// </summary>
    public AndGuard<T> And(Func<T, bool> predicate, string message, string? errorCode = null)
    {
        if (_shortCircuit && _errors.Count > 0)
        {
            return this; // Skip remaining validations
        }

        if (!predicate(_value))
        {
            _errors.Add(new ValidationError(_parameterName, message, errorCode));
        }
        return this;
    }

    /// <summary>
    /// Validates all conditions passed.
    /// </summary>
    public T Validate()
    {
        if (_errors.Count > 0)
        {
            throw new AggregateValidationException(_errors);
        }
        return _value;
    }

    /// <summary>
    /// Returns a result instead of throwing.
    /// </summary>
    public GuardResult ToResult()
    {
        return _errors.Count == 0 ? GuardResult.Success() : GuardResult.Failure(_errors);
    }
}

/// <summary>
/// Extension methods for creating logical guard chains.
/// </summary>
public static class LogicalGuardExtensions
{
    /// <summary>
    /// Creates an OR guard chain where any validation passing is sufficient.
    /// </summary>
    public static OrGuard<T> EitherOr<T>(this T value, string parameterName)
    {
        return new OrGuard<T>(value, parameterName);
    }

    /// <summary>
    /// Creates an AND guard chain where all validations must pass.
    /// </summary>
    public static AndGuard<T> AllOf<T>(this T value, string parameterName, bool shortCircuit = false)
    {
        return new AndGuard<T>(value, parameterName, shortCircuit);
    }
}
