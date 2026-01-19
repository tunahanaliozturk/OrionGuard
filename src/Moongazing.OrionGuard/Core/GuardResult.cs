using System.Collections.Immutable;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Represents the result of a validation operation with error accumulation support.
/// </summary>
public sealed class GuardResult
{
    private readonly List<ValidationError> _errors;

    public IReadOnlyList<ValidationError> Errors => _errors.AsReadOnly();
    public bool IsValid => _errors.Count == 0;
    public bool IsInvalid => !IsValid;

    private GuardResult()
    {
        _errors = new List<ValidationError>();
    }

    private GuardResult(IEnumerable<ValidationError> errors)
    {
        _errors = errors.ToList();
    }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static GuardResult Success() => new();

    /// <summary>
    /// Creates a failed validation result with a single error.
    /// </summary>
    public static GuardResult Failure(string parameterName, string message, string? errorCode = null)
        => new(new[] { new ValidationError(parameterName, message, errorCode) });

    /// <summary>
    /// Creates a failed validation result with multiple errors.
    /// </summary>
    public static GuardResult Failure(IEnumerable<ValidationError> errors) => new(errors);

    /// <summary>
    /// Combines multiple validation results into one.
    /// </summary>
    public static GuardResult Combine(params GuardResult[] results)
    {
        var allErrors = results.SelectMany(r => r.Errors).ToList();
        return allErrors.Count == 0 ? Success() : new GuardResult(allErrors);
    }

    /// <summary>
    /// Combines this result with another.
    /// </summary>
    public GuardResult Merge(GuardResult other)
    {
        return Combine(this, other);
    }

    /// <summary>
    /// Throws an AggregateException if validation failed.
    /// </summary>
    public void ThrowIfInvalid()
    {
        if (IsInvalid)
        {
            throw new AggregateValidationException(Errors);
        }
    }

    /// <summary>
    /// Returns formatted error messages.
    /// </summary>
    public string GetErrorSummary(string separator = "; ")
        => string.Join(separator, _errors.Select(e => e.Message));

    /// <summary>
    /// Converts to dictionary format (useful for API responses).
    /// </summary>
    public Dictionary<string, string[]> ToErrorDictionary()
        => _errors.GroupBy(e => e.ParameterName)
                  .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
}

/// <summary>
/// Represents a single validation error.
/// </summary>
public sealed record ValidationError(
    string ParameterName,
    string Message,
    string? ErrorCode = null
);

/// <summary>
/// Exception thrown when multiple validation errors occur.
/// </summary>
public sealed class AggregateValidationException : Exception
{
    public IReadOnlyList<ValidationError> Errors { get; }

    public AggregateValidationException(IEnumerable<ValidationError> errors)
        : base($"Validation failed with {errors.Count()} error(s).")
    {
        Errors = errors.ToList().AsReadOnly();
    }

    public override string ToString()
    {
        var errorDetails = string.Join(Environment.NewLine, Errors.Select(e => $"  - [{e.ParameterName}]: {e.Message}"));
        return $"{Message}{Environment.NewLine}{errorDetails}";
    }
}
