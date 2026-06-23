using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Hangfire;

/// <summary>
/// Thrown by <see cref="OrionGuardClientFilter"/> while a background job is being created when one
/// or more of the job's arguments fail OrionGuard validation. Hangfire surfaces an exception thrown
/// from a client filter's <c>OnCreating</c> to the caller that enqueued the job, so the invalid job
/// is rejected by the enqueue call instead of being scheduled and later failing inside a worker.
/// </summary>
/// <remarks>
/// The <see cref="Errors"/> collection carries every blocking <see cref="ValidationError"/> gathered
/// across all of the job's arguments in a single pass, mirroring the accumulation semantics of the
/// other OrionGuard integrations.
/// </remarks>
public sealed class JobArgumentValidationException : Exception
{
    /// <summary>The blocking validation errors gathered across the job's arguments.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>
    /// The declaring type of the job method whose arguments failed validation, when available.
    /// </summary>
    public Type? JobType { get; }

    /// <summary>
    /// The name of the job method whose arguments failed validation, when available.
    /// </summary>
    public string? MethodName { get; }

    /// <summary>Initializes a new instance of the <see cref="JobArgumentValidationException"/> class.</summary>
    /// <param name="errors">The blocking validation errors. Must not be <see langword="null"/>.</param>
    /// <param name="jobType">The declaring type of the job method, when available.</param>
    /// <param name="methodName">The job method name, when available.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="errors"/> is <see langword="null"/>.</exception>
    public JobArgumentValidationException(
        IEnumerable<ValidationError> errors,
        Type? jobType = null,
        string? methodName = null)
        : base(BuildMessage(errors, jobType, methodName))
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors.ToList().AsReadOnly();
        JobType = jobType;
        MethodName = methodName;
    }

    private static string BuildMessage(IEnumerable<ValidationError> errors, Type? jobType, string? methodName)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var count = errors.Count();
        var target =
            jobType is not null && methodName is not null ? $"{jobType.FullName}.{methodName}" :
            methodName ??
            jobType?.FullName;

        return target is null
            ? $"Job argument validation failed with {count} error(s)."
            : $"Job argument validation failed for '{target}' with {count} error(s).";
    }

    /// <inheritdoc />
    /// <remarks>
    /// Augments the standard <see cref="Exception.ToString"/> output rather than replacing it. The base
    /// implementation already emits the exception's type name and message header followed by the stack
    /// trace (and any inner exceptions); dropping that loses the information a developer most needs from a
    /// log. The per-argument validation details are appended after the base content so diagnostics carry
    /// both the full exception report and the specific blocking errors.
    /// </remarks>
    public override string ToString()
    {
        var details = string.Join(
            Environment.NewLine,
            Errors.Select(e => $"  - [{e.ParameterName}]: {e.Message}"));

        // base.ToString() = "<Type>: <Message>\r\n   <stack trace>" (plus inner exceptions, if any).
        // Keep it, then append the validation breakdown.
        return string.IsNullOrEmpty(details)
            ? base.ToString()
            : $"{base.ToString()}{Environment.NewLine}Validation errors:{Environment.NewLine}{details}";
    }
}
