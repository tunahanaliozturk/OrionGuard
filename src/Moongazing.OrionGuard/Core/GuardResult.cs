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
    // Null while the result carries no issue at all, which is the overwhelmingly common case: a passing
    // validation then costs no list. Never an empty list, so "no issues" is a single reference check.
    private readonly List<ValidationError>? _issues;

    /// <summary>
    /// Errors that block the operation (<see cref="Severity.Error"/> only).
    /// </summary>
    public IReadOnlyList<ValidationError> Errors => OfSeverity(Severity.Error);

    /// <summary>
    /// Non-blocking advisories (<see cref="Severity.Warning"/>).
    /// </summary>
    public IReadOnlyList<ValidationError> Warnings => OfSeverity(Severity.Warning);

    /// <summary>
    /// Informational notices (<see cref="Severity.Info"/>).
    /// </summary>
    public IReadOnlyList<ValidationError> Infos => OfSeverity(Severity.Info);

    /// <summary>
    /// All issues regardless of severity.
    /// </summary>
    public IReadOnlyList<ValidationError> AllIssues =>
        _issues is null ? Array.Empty<ValidationError>() : _issues.AsReadOnly();

    private IReadOnlyList<ValidationError> OfSeverity(Severity severity)
    {
        if (_issues is null)
        {
            return Array.Empty<ValidationError>();
        }

        List<ValidationError>? matches = null;
        foreach (var issue in _issues)
        {
            if (issue.Severity == severity)
            {
                (matches ??= new()).Add(issue);
            }
        }

        return matches is null ? Array.Empty<ValidationError>() : matches.AsReadOnly();
    }

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
            if (_issues is null) return false;
            foreach (var issue in _issues)
                if (issue.Severity == Severity.Warning) return true;
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasErrorSeverity()
    {
        if (_issues is null) return false;
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
    }

    private GuardResult(IEnumerable<ValidationError> issues)
    {
        var copy = issues.ToList();
        _issues = copy.Count == 0 ? null : copy;
    }

    // A success carries no state to leak: it holds no issues, and the only way to set
    // SuggestedHttpStatusCode is a constructor this type keeps private. So every caller can share one.
    private static readonly GuardResult SuccessResult = new();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static GuardResult Success() => SuccessResult;

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
    /// from every input are preserved, and the first non-null <see cref="SuggestedHttpStatusCode"/>
    /// (in argument order) is carried over.
    /// </summary>
    public static GuardResult Combine(params GuardResult[] results)
    {
        int total = 0;
        int? statusCode = null;
        foreach (var result in results)
        {
            total += result._issues?.Count ?? 0;
            statusCode ??= result.SuggestedHttpStatusCode;
        }

        if (total == 0 && statusCode is null) return Success();

        var all = new List<ValidationError>(total);
        foreach (var result in results)
            if (result._issues is not null)
                all.AddRange(result._issues);

        return new GuardResult(all) { SuggestedHttpStatusCode = statusCode };
    }

    /// <summary>
    /// Combines this result with another. This result's <see cref="SuggestedHttpStatusCode"/> wins when
    /// both carry one.
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
        => _issues is null
            ? string.Empty
            : string.Join(separator, _issues
                .Where(e => e.Severity == Severity.Error)
                .Select(e => e.Message));

    /// <summary>
    /// Converts errors into a dictionary format suitable for API responses. Only
    /// <see cref="Severity.Error"/> entries are included (backward-compatible).
    /// </summary>
    public Dictionary<string, string[]> ToErrorDictionary()
        => _issues is null
            ? new Dictionary<string, string[]>()
            : _issues.Where(e => e.Severity == Severity.Error)
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
        : this(errors.ToList())
    {
    }

    // Materialize once: the message needs the count and the property needs the items, and a lazy
    // sequence would otherwise be enumerated twice -- re-running whatever produced it.
    private AggregateValidationException(List<ValidationError> errors)
        : base($"Validation failed with {errors.Count} error(s).")
    {
        Errors = errors.AsReadOnly();
    }

    public override string ToString()
    {
        var errorDetails = string.Join(Environment.NewLine, Errors.Select(e => $"  - [{e.ParameterName}]: {e.Message}"));
        return $"{Message}{Environment.NewLine}{errorDetails}";
    }
}
