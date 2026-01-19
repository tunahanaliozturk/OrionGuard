using System.Diagnostics;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Provides performance-optimized guard methods that can be conditionally compiled out in release builds.
/// These guards are meant for development-time assertions and can be stripped in production.
/// </summary>
public static class DebugGuard
{
    /// <summary>
    /// Validates condition only in DEBUG builds. No-op in RELEASE.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Debug assertion failed: {message}");
        }
    }

    /// <summary>
    /// Validates value is not null only in DEBUG builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void NotNull<T>(T? value, string parameterName) where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName, $"Debug guard: {parameterName} cannot be null.");
        }
    }

    /// <summary>
    /// Validates condition with custom exception only in DEBUG builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Require(bool condition, Func<Exception> exceptionFactory)
    {
        if (!condition)
        {
            throw exceptionFactory();
        }
    }
}

/// <summary>
/// Provides contract-based precondition and postcondition checking.
/// Inspired by Code Contracts pattern.
/// </summary>
public static class Contract
{
    /// <summary>
    /// Specifies a precondition contract for the enclosing method.
    /// </summary>
    /// <param name="condition">The conditional expression to test.</param>
    /// <param name="message">The message to display if the condition is false.</param>
    /// <exception cref="ContractException">Thrown when condition is false.</exception>
    public static void Requires(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new ContractException($"Precondition failed: {message ?? "Condition was not met."}");
        }
    }

    /// <summary>
    /// Specifies a precondition contract and returns the validated value.
    /// </summary>
    public static T Requires<T>(T value, Func<T, bool> predicate, string? message = null)
    {
        if (!predicate(value))
        {
            throw new ContractException($"Precondition failed: {message ?? "Condition was not met."}");
        }
        return value;
    }

    /// <summary>
    /// Specifies that a value cannot be null.
    /// </summary>
    public static T RequiresNotNull<T>(T? value, string? parameterName = null) where T : class
    {
        if (value is null)
        {
            throw new ContractException($"Precondition failed: {parameterName ?? "Value"} cannot be null.");
        }
        return value;
    }

    /// <summary>
    /// Specifies that a nullable value type cannot be null.
    /// </summary>
    public static T RequiresNotNull<T>(T? value, string? parameterName = null) where T : struct
    {
        if (!value.HasValue)
        {
            throw new ContractException($"Precondition failed: {parameterName ?? "Value"} cannot be null.");
        }
        return value.Value;
    }

    /// <summary>
    /// Specifies a postcondition contract. Should be called at the end of a method.
    /// </summary>
    public static void Ensures(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new ContractException($"Postcondition failed: {message ?? "Condition was not met."}");
        }
    }

    /// <summary>
    /// Specifies a postcondition that validates and returns the result.
    /// </summary>
    public static T Ensures<T>(T value, Func<T, bool> predicate, string? message = null)
    {
        if (!predicate(value))
        {
            throw new ContractException($"Postcondition failed: {message ?? "Condition was not met."}");
        }
        return value;
    }

    /// <summary>
    /// Specifies an invariant condition that must always be true.
    /// </summary>
    public static void Invariant(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new ContractException($"Invariant violated: {message ?? "Condition was not met."}");
        }
    }
}

/// <summary>
/// Exception thrown when a contract is violated.
/// </summary>
public sealed class ContractException : Exception
{
    public ContractException(string message) : base(message) { }
    public ContractException(string message, Exception innerException) : base(message, innerException) { }
}
