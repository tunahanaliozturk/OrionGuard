using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Represents the result of a validation operation with error accumulation support.
/// </summary>
public sealed class GuardResult
{
    private readonly List<ValidationError> _errors;

    public IReadOnlyList<ValidationError> Errors => _errors.AsReadOnly();

    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _errors.Count == 0;
    }

    public bool IsInvalid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _errors.Count != 0;
    }

    /// <summary>
    /// Optional HTTP status code hint for ASP.NET Core ProblemDetails responses.
    /// Default is null (middleware will use 422 Unprocessable Entity).
    /// </summary>
    public int? SuggestedHttpStatusCode { get; init; }

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
    /// Creates a failed validation result with a suggested HTTP status code
    /// for ASP.NET Core ProblemDetails integration.
    /// </summary>
    public static GuardResult FailureWithStatus(int httpStatusCode, string parameterName, string message, string? errorCode = null)
        => new(new[] { new ValidationError(parameterName, message, errorCode) })
        {
            SuggestedHttpStatusCode = httpStatusCode
        };

    /// <summary>
    /// Combines multiple validation results into one.
    /// </summary>
    public static GuardResult Combine(params GuardResult[] results)
    {
        // Fast path: check if any result has errors without materializing
        bool hasErrors = false;
        int totalErrors = 0;
        foreach (var result in results)
        {
            if (result.IsInvalid)
            {
                hasErrors = true;
                totalErrors += result.Errors.Count;
            }
        }

        if (!hasErrors) return Success();

        var allErrors = new List<ValidationError>(totalErrors);
        foreach (var result in results)
        {
            if (result.IsInvalid)
                allErrors.AddRange(result.Errors);
        }
        return new GuardResult(allErrors);
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
