using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.MassTransit;

/// <summary>
/// Thrown by <see cref="OrionGuardConsumeFilter{TMessage}"/> when a consumed message fails OrionGuard
/// validation. The exception faults the message before it reaches the consumer, so MassTransit's
/// standard error handling applies: a <c>Fault&lt;TMessage&gt;</c> is published and the message is moved
/// to the endpoint's <c>_error</c> queue.
/// </summary>
/// <remarks>
/// The <see cref="Errors"/> collection carries every blocking <see cref="ValidationError"/> from every
/// validator registered for the message type, gathered in a single pass. Validation failures are
/// deterministic, so a retry policy should not retry them; see the package README for
/// <c>r.Ignore&lt;MessageValidationException&gt;()</c>.
/// </remarks>
public sealed class MessageValidationException : Exception
{
    /// <summary>The blocking validation errors gathered across the message's validators.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>The consumed message type that failed validation, when available.</summary>
    public Type? MessageType { get; }

    /// <summary>Initializes a new instance of the <see cref="MessageValidationException"/> class.</summary>
    /// <param name="errors">The blocking validation errors. Must not be <see langword="null"/>.</param>
    /// <param name="messageType">The consumed message type, when available.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="errors"/> is <see langword="null"/>.</exception>
    public MessageValidationException(IEnumerable<ValidationError> errors, Type? messageType = null)
        : base(BuildMessage(errors, messageType))
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = errors.ToList().AsReadOnly();
        MessageType = messageType;
    }

    private static string BuildMessage(IEnumerable<ValidationError> errors, Type? messageType)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var count = errors.Count();
        return messageType is null
            ? $"Message validation failed with {count} error(s)."
            : $"Message validation failed for '{messageType.FullName}' with {count} error(s).";
    }

    /// <inheritdoc />
    /// <remarks>
    /// Keeps the standard <see cref="Exception.ToString"/> output (type, message, stack trace, inner
    /// exceptions) and appends one line per validation error, so a logged fault carries both the full
    /// exception report and the specific blocking errors.
    /// </remarks>
    public override string ToString()
    {
        var details = string.Join(
            Environment.NewLine,
            Errors.Select(e => $"  - [{e.ParameterName}]: {e.Message}"));

        return string.IsNullOrEmpty(details)
            ? base.ToString()
            : $"{base.ToString()}{Environment.NewLine}Validation errors:{Environment.NewLine}{details}";
    }
}
