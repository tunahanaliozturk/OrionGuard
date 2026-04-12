using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Severity level for a <see cref="ValidationError"/>. Allows validation results to carry
/// non-blocking information (warnings, informational notices) alongside blocking errors.
/// </summary>
public enum Severity
{
    /// <summary>Blocks the operation. Contributes to <see cref="GuardResult.IsInvalid"/>.</summary>
    Error = 0,

    /// <summary>Non-blocking advisory. Surfaced via <see cref="GuardResult.Warnings"/>.</summary>
    Warning = 1,

    /// <summary>Informational only. Surfaced via <see cref="GuardResult.Infos"/>.</summary>
    Info = 2,
}

/// <summary>
/// Represents the result of a validation operation with error accumulation support.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="GuardResult"/> holds a flat list of <see cref="ValidationError"/> entries,
/// each tagged with a <see cref="Severity"/>. Convenience properties filter the list:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="Errors"/> -- entries with <see cref="Severity.Error"/> only (backward-compatible).</description></item>
/// <item><description><see cref="Warnings"/> -- entries with <see cref="Severity.Warning"/>.</description></item>
/// <item><description><see cref="Infos"/> -- entries with <see cref="Severity.Info"/>.</description></item>
/// <item><description><see cref="AllIssues"/> -- everything regardless of severity.</description></item>
/// </list>
/// <para>
/// <see cref="IsInvalid"/> is driven by <see cref="Severity.Error"/> entries only, so a
/// result carrying only warnings is still considered valid.
/// </para>
/// </remarks>
public sealed class GuardResult
{
    private readonly List<ValidationError> _issues;

    /// <summary>
    /// Errors that block the operation (<see cref="Severity.Error"/> only).
    /// </summary>
    public IReadOnlyList<ValidationError> Errors =>
        _issues.Where(e => e.Severity == Severity.Error).ToList().AsReadOnly();

    /// <summary>
    /// Non-blocking advisories (<see cref="Severity.Warning"/>).
    /// </summary>
    public IReadOnlyList<ValidationError> Warnings =>
        _issues.Where(e => e.Severity == Severity.Warning).ToList().AsReadOnly();

    /// <summary>
    /// Informational notices (<see cref="Severity.Info"/>).
    /// </summary>
    public IReadOnlyList<ValidationError> Infos =>
        _issues.Where(e => e.Severity == Severity.Info).ToList().AsReadOnly();

    /// <summary>
    /// All issues regardless of severity.
    /// </summary>
    public IReadOnlyList<ValidationError> AllIssues => _issues.AsReadOnly();

    public bool IsValid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !HasErrorSeverity();
    }

    public bool IsInvalid
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => HasErrorSeverity();
    }

    /// <summary>
    /// Returns <c>true</c> if any <see cref="Severity.Warning"/> entries are present.
    /// </summary>
    public bool HasWarnings
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            foreach (var issue in _issues)
                if (issue.Severity == Severity.Warning) return true;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasErrorSeverity()
    {
        foreach (var issue in _issues)
            if (issue.Severity == Severity.Error) return true;
        return false;
    }

    /// <summary>
    /// Optional HTTP status code hint for ASP.NET Core ProblemDetails responses.
    /// Default is null (middleware will use 422 Unprocessable Entity).
    /// </summary>
    public int? SuggestedHttpStatusCode { get; init; }

    private GuardResult()
    {
        _issues = new List<ValidationError>();
    }

    private GuardResult(IEnumerable<ValidationError> issues)
    {
        _issues = issues.ToList();
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
    /// Combines multiple validation results into one. All issues (errors, warnings, infos)
    /// from every input are preserved.
    /// </summary>
    public static GuardResult Combine(params GuardResult[] results)
    {
        int total = 0;
        foreach (var result in results)
            total += result._issues.Count;

        if (total == 0) return Success();

        var all = new List<ValidationError>(total);
        foreach (var result in results)
            all.AddRange(result._issues);

        return new GuardResult(all);
    }

    /// <summary>
    /// Combines this result with another.
    /// </summary>
    public GuardResult Merge(GuardResult other) => Combine(this, other);

    /// <summary>
    /// Throws an <see cref="AggregateValidationException"/> if validation produced any
    /// <see cref="Severity.Error"/> entries. Warnings and infos do not throw.
    /// </summary>
    public void ThrowIfInvalid()
    {
        if (IsInvalid)
        {
            throw new AggregateValidationException(Errors);
        }
    }

    /// <summary>
    /// Returns a formatted summary of error messages (warnings/infos excluded by default).
    /// </summary>
    public string GetErrorSummary(string separator = "; ")
        => string.Join(separator, _issues
            .Where(e => e.Severity == Severity.Error)
            .Select(e => e.Message));

    /// <summary>
    /// Converts errors into a dictionary format suitable for API responses. Only
    /// <see cref="Severity.Error"/> entries are included (backward-compatible).
    /// </summary>
    public Dictionary<string, string[]> ToErrorDictionary()
        => _issues.Where(e => e.Severity == Severity.Error)
                  .GroupBy(e => e.ParameterName)
                  .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
}

/// <summary>
/// Represents a single validation issue. Defaults to <see cref="Core.Severity.Error"/>
/// for backward compatibility -- rules that want to emit non-blocking advisories should
/// set <see cref="Severity"/> to <see cref="Core.Severity.Warning"/> or <see cref="Core.Severity.Info"/>.
/// </summary>
public sealed record ValidationError(
    string ParameterName,
    string Message,
    string? ErrorCode = null,
    Severity Severity = Severity.Error
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
