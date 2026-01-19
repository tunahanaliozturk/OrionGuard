using System.Runtime.CompilerServices;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Main entry point for v4.0 fluent guard API with CallerArgumentExpression support.
/// </summary>
public static class Ensure
{
    /// <summary>
    /// Creates a fluent guard for the specified value (throws on first error).
    /// Parameter name is automatically captured using CallerArgumentExpression.
    /// </summary>
    /// <example>
    /// Ensure.That(email).NotNull().NotEmpty().Email();
    /// </example>
    public static FluentGuard<T> That<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        return new FluentGuard<T>(value, parameterName ?? "value", throwOnFirstError: true);
    }

    /// <summary>
    /// Creates a fluent guard that accumulates all errors instead of throwing on first error.
    /// </summary>
    /// <example>
    /// var result = Ensure.Accumulate(user.Email).NotNull().Email().ToResult();
    /// </example>
    public static FluentGuard<T> Accumulate<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        return new FluentGuard<T>(value, parameterName ?? "value", throwOnFirstError: false);
    }

    /// <summary>
    /// Creates a fluent guard with explicit parameter name (for when CallerArgumentExpression isn't suitable).
    /// </summary>
    public static FluentGuard<T> For<T>(T value, string parameterName)
    {
        return new FluentGuard<T>(value, parameterName, throwOnFirstError: true);
    }

    /// <summary>
    /// Validates multiple values and combines their results.
    /// </summary>
    /// <example>
    /// var result = Ensure.All(
    ///     Ensure.Accumulate(email).Email(),
    ///     Ensure.Accumulate(password).MinLength(8)
    /// );
    /// </example>
    public static GuardResult All(params FluentGuard<object>[] guards)
    {
        var results = guards.Select(g => g.ToResult()).ToArray();
        return GuardResult.Combine(results);
    }

    /// <summary>
    /// Validates a value is not null and returns it (null guard shorthand).
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when value is null.</exception>
    public static T NotNull<T>(
        T? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null) where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }
        return value;
    }

    /// <summary>
    /// Validates a value is not null and returns it (null guard shorthand for value types).
    /// </summary>
    public static T NotNull<T>(
        T? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null) where T : struct
    {
        if (!value.HasValue)
        {
            throw new ArgumentNullException(parameterName);
        }
        return value.Value;
    }

    /// <summary>
    /// Validates a string is not null or empty and returns it.
    /// </summary>
    public static string NotNullOrEmpty(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty.", parameterName);
        }
        return value;
    }

    /// <summary>
    /// Validates a string is not null, empty, or whitespace and returns it.
    /// </summary>
    public static string NotNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be null, empty, or whitespace.", parameterName);
        }
        return value;
    }

    /// <summary>
    /// Validates a value is within specified range and returns it.
    /// </summary>
    public static T InRange<T>(
        T value,
        T min,
        T max,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null) where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be between {min} and {max}.");
        }
        return value;
    }
}
