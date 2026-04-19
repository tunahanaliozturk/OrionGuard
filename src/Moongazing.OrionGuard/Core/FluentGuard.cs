using System.Runtime.CompilerServices;
using Moongazing.OrionGuard.Exceptions;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Modern fluent guard builder with enhanced features for v4.0
/// Supports: Method chaining, Result pattern, Custom messages, Conditional validation
/// </summary>
public sealed class FluentGuard<T>
{
    private readonly T _value;
    private readonly string _parameterName;
    private readonly List<ValidationError> _errors;
    private readonly bool _throwOnFirstError;
    private bool _shouldValidate = true;

    internal FluentGuard(T value, string parameterName, bool throwOnFirstError = true)
    {
        _value = value;
        _parameterName = parameterName;
        _errors = new List<ValidationError>();
        _throwOnFirstError = throwOnFirstError;
    }

    /// <summary>
    /// Gets the validated value.
    /// </summary>
    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value;
    }

    /// <summary>
    /// Gets the parameter name being validated.
    /// </summary>
    public string ParameterName => _parameterName;

    #region Conditional Validation

    /// <summary>
    /// Only applies subsequent validations when the condition is true.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FluentGuard<T> When(bool condition)
    {
        _shouldValidate = condition;
        return this;
    }

    /// <summary>
    /// Only applies subsequent validations when the condition is true.
    /// </summary>
    public FluentGuard<T> When(Func<T, bool> condition)
    {
        _shouldValidate = condition(_value);
        return this;
    }

    /// <summary>
    /// Only applies subsequent validations when the condition is false.
    /// </summary>
    public FluentGuard<T> Unless(bool condition) => When(!condition);

    /// <summary>
    /// Only applies subsequent validations when the condition is false.
    /// </summary>
    public FluentGuard<T> Unless(Func<T, bool> condition)
    {
        _shouldValidate = !condition(_value);
        return this;
    }

    /// <summary>
    /// Resets conditional validation - all subsequent validations will run.
    /// </summary>
    public FluentGuard<T> Always()
    {
        _shouldValidate = true;
        return this;
    }

    #endregion

    #region Core Validations

    /// <summary>
    /// Validates that the value is not null.
    /// </summary>
    public FluentGuard<T> NotNull(string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is null)
        {
            AddError(message ?? $"{_parameterName} cannot be null.", "NOT_NULL");
        }
        return this;
    }

    /// <summary>
    /// Validates that the value is not default.
    /// </summary>
    public FluentGuard<T> NotDefault(string? message = null)
    {
        if (!_shouldValidate) return this;

        if (EqualityComparer<T>.Default.Equals(_value, default!))
        {
            AddError(message ?? $"{_parameterName} cannot be default value.", "NOT_DEFAULT");
        }
        return this;
    }

    /// <summary>
    /// Validates using a custom predicate.
    /// </summary>
    public FluentGuard<T> Must(Func<T, bool> predicate, string message, string? errorCode = null)
    {
        if (!_shouldValidate) return this;

        if (!predicate(_value))
        {
            AddError(message, errorCode ?? "CUSTOM");
        }
        return this;
    }

    /// <summary>
    /// Validates using a custom predicate with lazy message.
    /// </summary>
    public FluentGuard<T> Must(Func<T, bool> predicate, Func<T, string> messageFactory, string? errorCode = null)
    {
        if (!_shouldValidate) return this;

        if (!predicate(_value))
        {
            AddError(messageFactory(_value), errorCode ?? "CUSTOM");
        }
        return this;
    }

    #endregion

    #region String Validations

    /// <summary>
    /// Validates that the string is not empty or whitespace.
    /// </summary>
    public FluentGuard<T> NotEmpty(string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is string str && string.IsNullOrWhiteSpace(str))
        {
            AddError(message ?? $"{_parameterName} cannot be empty or whitespace.", "NOT_EMPTY");
        }
        else if (_value is System.Collections.IEnumerable enumerable && !HasAnyItems(enumerable))
        {
            AddError(message ?? $"{_parameterName} cannot be empty.", "NOT_EMPTY");
        }
        return this;
    }

    /// <summary>
    /// Validates string length is within range.
    /// </summary>
    public FluentGuard<T> Length(int min, int max, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is string str && (str.Length < min || str.Length > max))
        {
            AddError(message ?? $"{_parameterName} must be between {min} and {max} characters.", "LENGTH");
        }
        return this;
    }

    /// <summary>
    /// Validates string has minimum length.
    /// </summary>
    public FluentGuard<T> MinLength(int min, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is string str && str.Length < min)
        {
            AddError(message ?? $"{_parameterName} must be at least {min} characters.", "MIN_LENGTH");
        }
        return this;
    }

    /// <summary>
    /// Validates string has maximum length.
    /// </summary>
    public FluentGuard<T> MaxLength(int max, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is string str && str.Length > max)
        {
            AddError(message ?? $"{_parameterName} must be at most {max} characters.", "MAX_LENGTH");
        }
        return this;
    }

    /// <summary>
    /// Validates string matches regex pattern.
    /// </summary>
    public FluentGuard<T> Matches(string pattern, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is string str && !RegexCache.IsMatch(str, pattern))
        {
            AddError(message ?? $"{_parameterName} does not match the required pattern.", "PATTERN");
        }
        return this;
    }

    /// <summary>
    /// Validates string is a valid email.
    /// </summary>
    public FluentGuard<T> Email(string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is string str && !Utilities.GeneratedRegexPatterns.Email().IsMatch(str))
        {
            AddError(message ?? $"{_parameterName} must be a valid email address.", "INVALID_EMAIL");
        }
        return this;
    }

    /// <summary>
    /// Validates string is a valid URL.
    /// </summary>
    public FluentGuard<T> Url(string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is string str && !Uri.TryCreate(str, UriKind.Absolute, out _))
        {
            AddError(message ?? $"{_parameterName} must be a valid URL.", "URL");
        }
        return this;
    }

    /// <summary>
    /// Validates string starts with the specified value.
    /// </summary>
    public FluentGuard<T> StartsWith(string prefix, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is string str && !str.StartsWith(prefix, StringComparison.Ordinal))
        {
            AddError(message ?? $"{_parameterName} must start with '{prefix}'.", "STARTS_WITH");
        }
        return this;
    }

    /// <summary>
    /// Validates string ends with the specified value.
    /// </summary>
    public FluentGuard<T> EndsWith(string suffix, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is string str && !str.EndsWith(suffix, StringComparison.Ordinal))
        {
            AddError(message ?? $"{_parameterName} must end with '{suffix}'.", "ENDS_WITH");
        }
        return this;
    }

    /// <summary>
    /// Validates string contains the specified value.
    /// </summary>
    public FluentGuard<T> Contains(string substring, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is string str && !str.Contains(substring))
        {
            AddError(message ?? $"{_parameterName} must contain '{substring}'.", "CONTAINS");
        }
        return this;
    }

    #endregion

    #region Numeric Validations

    /// <summary>
    /// Validates numeric value is greater than specified minimum.
    /// </summary>
    public FluentGuard<T> GreaterThan<TValue>(TValue min, string? message = null) where TValue : IComparable<TValue>
    {
        if (!_shouldValidate) return this;

        if (_value is TValue val && val.CompareTo(min) <= 0)
        {
            AddError(message ?? $"{_parameterName} must be greater than {min}.", "GREATER_THAN");
        }
        return this;
    }

    /// <summary>
    /// Validates numeric value is less than specified maximum.
    /// </summary>
    public FluentGuard<T> LessThan<TValue>(TValue max, string? message = null) where TValue : IComparable<TValue>
    {
        if (!_shouldValidate) return this;

        if (_value is TValue val && val.CompareTo(max) >= 0)
        {
            AddError(message ?? $"{_parameterName} must be less than {max}.", "LESS_THAN");
        }
        return this;
    }

    /// <summary>
    /// Validates numeric value is within range (inclusive).
    /// </summary>
    public FluentGuard<T> InRange<TValue>(TValue min, TValue max, string? message = null) where TValue : IComparable<TValue>
    {
        if (!_shouldValidate) return this;

        if (_value is TValue val && (val.CompareTo(min) < 0 || val.CompareTo(max) > 0))
        {
            AddError(message ?? $"{_parameterName} must be between {min} and {max}.", "IN_RANGE");
        }
        return this;
    }

    /// <summary>
    /// Validates numeric value is positive.
    /// </summary>
    public FluentGuard<T> Positive(string? message = null)
    {
        if (!_shouldValidate) return this;

        bool isNegativeOrZero = _value switch
        {
            int i => i <= 0,
            long l => l <= 0,
            decimal d => d <= 0,
            double dbl => dbl <= 0,
            float f => f <= 0,
            _ => false
        };

        if (isNegativeOrZero)
        {
            AddError(message ?? $"{_parameterName} must be positive.", "POSITIVE");
        }
        return this;
    }

    /// <summary>
    /// Validates numeric value is not negative.
    /// </summary>
    public FluentGuard<T> NotNegative(string? message = null)
    {
        if (!_shouldValidate) return this;

        bool isNegative = _value switch
        {
            int i => i < 0,
            long l => l < 0,
            decimal d => d < 0,
            double dbl => dbl < 0,
            float f => f < 0,
            _ => false
        };

        if (isNegative)
        {
            AddError(message ?? $"{_parameterName} cannot be negative.", "NOT_NEGATIVE");
        }
        return this;
    }

    /// <summary>
    /// Validates numeric value is not zero.
    /// </summary>
    public FluentGuard<T> NotZero(string? message = null)
    {
        if (!_shouldValidate) return this;

        bool isZero = _value switch
        {
            int i => i == 0,
            long l => l == 0,
            decimal d => d == 0,
            double dbl => dbl == 0,
            float f => f == 0,
            _ => false
        };

        if (isZero)
        {
            AddError(message ?? $"{_parameterName} cannot be zero.", "NOT_ZERO");
        }
        return this;
    }

    #endregion

    #region Collection Validations

    /// <summary>
    /// Validates collection has specified count.
    /// </summary>
    public FluentGuard<T> Count(int expected, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is System.Collections.IEnumerable enumerable)
        {
            var count = CountItems(enumerable);
            if (count != expected)
            {
                AddError(message ?? $"{_parameterName} must have exactly {expected} items.", "COUNT");
            }
        }
        return this;
    }

    /// <summary>
    /// Validates collection has minimum count.
    /// </summary>
    public FluentGuard<T> MinCount(int min, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is System.Collections.IEnumerable enumerable)
        {
            var count = CountItems(enumerable);
            if (count < min)
            {
                AddError(message ?? $"{_parameterName} must have at least {min} items.", "MIN_COUNT");
            }
        }
        return this;
    }

    /// <summary>
    /// Validates collection has maximum count.
    /// </summary>
    public FluentGuard<T> MaxCount(int max, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is System.Collections.IEnumerable enumerable)
        {
            var count = CountItems(enumerable);
            if (count > max)
            {
                AddError(message ?? $"{_parameterName} must have at most {max} items.", "MAX_COUNT");
            }
        }
        return this;
    }

    /// <summary>
    /// Validates all items in collection satisfy the predicate.
    /// </summary>
    public FluentGuard<T> All<TItem>(Func<TItem, bool> predicate, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is IEnumerable<TItem> items && !items.All(predicate))
        {
            AddError(message ?? $"All items in {_parameterName} must satisfy the condition.", "ALL");
        }
        return this;
    }

    /// <summary>
    /// Validates no null items in collection.
    /// </summary>
    public FluentGuard<T> NoNullItems(string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is System.Collections.IEnumerable enumerable && HasNullItem(enumerable))
        {
            AddError(message ?? $"{_parameterName} cannot contain null items.", "NO_NULL_ITEMS");
        }
        return this;
    }

    #endregion

    #region Date Validations

    /// <summary>
    /// Validates date is in the past.
    /// </summary>
    public FluentGuard<T> InPast(string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is DateTime dt && dt >= DateTime.UtcNow)
        {
            AddError(message ?? $"{_parameterName} must be in the past.", "IN_PAST");
        }
        else if (_value is DateOnly d && d >= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            AddError(message ?? $"{_parameterName} must be in the past.", "IN_PAST");
        }
        return this;
    }

    /// <summary>
    /// Validates date is in the future.
    /// </summary>
    public FluentGuard<T> InFuture(string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is DateTime dt && dt <= DateTime.UtcNow)
        {
            AddError(message ?? $"{_parameterName} must be in the future.", "IN_FUTURE");
        }
        else if (_value is DateOnly d && d <= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            AddError(message ?? $"{_parameterName} must be in the future.", "IN_FUTURE");
        }
        return this;
    }

    /// <summary>
    /// Validates date is within specified range.
    /// </summary>
    public FluentGuard<T> DateBetween(DateTime start, DateTime end, string? message = null)
    {
        if (!_shouldValidate) return this;

        if (_value is DateTime dt && (dt < start || dt > end))
        {
            AddError(message ?? $"{_parameterName} must be between {start:d} and {end:d}.", "DATE_BETWEEN");
        }
        return this;
    }

    #endregion

    #region Transform Operations

    /// <summary>
    /// Transforms the value (e.g., trim, lowercase) during validation.
    /// Creates a new FluentGuard with the transformed value.
    /// </summary>
    public FluentGuard<T> Transform(Func<T, T> transform)
    {
        if (!_shouldValidate) return this;

        var transformed = transform(_value);
        var guard = new FluentGuard<T>(transformed, _parameterName, _throwOnFirstError);
        guard._shouldValidate = _shouldValidate;
        foreach (var error in _errors)
            guard._errors.Add(error);
        return guard;
    }

    /// <summary>
    /// Returns a default value if the current value is null.
    /// </summary>
    public FluentGuard<T> Default(T defaultValue)
    {
        if (_value is null)
        {
            var guard = new FluentGuard<T>(defaultValue, _parameterName, _throwOnFirstError);
            guard._shouldValidate = _shouldValidate;
            foreach (var error in _errors)
                guard._errors.Add(error);
            return guard;
        }
        return this;
    }

    #endregion

    #region Result Methods

    /// <summary>
    /// Returns the validated value (throws if validation failed and throwOnFirstError is true).
    /// </summary>
    public T Build()
    {
        if (_errors.Count > 0 && _throwOnFirstError)
        {
            throw new AggregateValidationException(_errors);
        }
        return _value;
    }

    /// <summary>
    /// Returns validation result without throwing.
    /// </summary>
    public GuardResult ToResult()
    {
        return _errors.Count == 0 ? GuardResult.Success() : GuardResult.Failure(_errors);
    }

    /// <summary>
    /// Tries to validate and returns success status.
    /// </summary>
    public bool TryValidate(out T value, out IReadOnlyList<ValidationError> errors)
    {
        value = _value;
        errors = _errors.AsReadOnly();
        return _errors.Count == 0;
    }

    /// <summary>
    /// Implicit conversion to the value type.
    /// </summary>
    public static implicit operator T(FluentGuard<T> guard) => guard.Build();

    #endregion

    #region Private Methods

    private void AddError(string message, string? errorCode)
    {
        var error = new ValidationError(_parameterName, message, errorCode);
        _errors.Add(error);

        if (_throwOnFirstError)
        {
            throw new GuardException(message);
        }
    }

    private static bool HasAnyItems(System.Collections.IEnumerable enumerable)
    {
        var enumerator = enumerable.GetEnumerator();
        try { return enumerator.MoveNext(); }
        finally { (enumerator as IDisposable)?.Dispose(); }
    }

    private static int CountItems(System.Collections.IEnumerable enumerable)
    {
        if (enumerable is System.Collections.ICollection collection) return collection.Count;
        int count = 0;
        var enumerator = enumerable.GetEnumerator();
        try { while (enumerator.MoveNext()) count++; }
        finally { (enumerator as IDisposable)?.Dispose(); }
        return count;
    }

    private static bool HasNullItem(System.Collections.IEnumerable enumerable)
    {
        var enumerator = enumerable.GetEnumerator();
        try
        {
            while (enumerator.MoveNext())
            {
                if (enumerator.Current is null) return true;
            }
            return false;
        }
        finally { (enumerator as IDisposable)?.Dispose(); }
    }

    #endregion
}
