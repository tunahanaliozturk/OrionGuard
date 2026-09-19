using System.Diagnostics;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.OpenTelemetry;

/// <summary>
/// Decorator that wraps an <see cref="IValidator{T}"/> with OpenTelemetry instrumentation,
/// recording validation metrics (count, failures, duration) and distributed tracing spans.
/// </summary>
/// <remarks>
/// Every <see cref="IValidator{T}"/> overload is implemented and forwarded to the same overload of the
/// inner validator, so a <see cref="ValidationContext"/> reaches the inner validator's context-aware rules
/// instead of being dropped by the interface's default implementation.
/// </remarks>
public sealed class InstrumentedValidator<T> : IValidator<T>
{
    private readonly IValidator<T> _inner;

    public InstrumentedValidator(IValidator<T> inner)
    {
        _inner = inner;
    }

    public GuardResult Validate(T value)
    {
        using var activity = StartActivity("OrionGuard.Validate");

        var startTimestamp = Stopwatch.GetTimestamp();
        var result = _inner.Validate(value);
        RecordMetrics(result, Stopwatch.GetElapsedTime(startTimestamp), activity);
        return result;
    }

    public GuardResult Validate(T value, ValidationContext context)
    {
        using var activity = StartActivity("OrionGuard.Validate");

        var startTimestamp = Stopwatch.GetTimestamp();
        var result = _inner.Validate(value, context);
        RecordMetrics(result, Stopwatch.GetElapsedTime(startTimestamp), activity);
        return result;
    }

    public async Task<GuardResult> ValidateAsync(T value, CancellationToken cancellationToken = default)
    {
        using var activity = StartActivity("OrionGuard.ValidateAsync");

        var startTimestamp = Stopwatch.GetTimestamp();
        var result = await _inner.ValidateAsync(value, cancellationToken).ConfigureAwait(false);
        RecordMetrics(result, Stopwatch.GetElapsedTime(startTimestamp), activity);
        return result;
    }

    public async Task<GuardResult> ValidateAsync(T value, ValidationContext context, CancellationToken cancellationToken = default)
    {
        using var activity = StartActivity("OrionGuard.ValidateAsync");

        var startTimestamp = Stopwatch.GetTimestamp();
        var result = await _inner.ValidateAsync(value, context, cancellationToken).ConfigureAwait(false);
        RecordMetrics(result, Stopwatch.GetElapsedTime(startTimestamp), activity);
        return result;
    }

    private static Activity? StartActivity(string name)
    {
        var activity = OrionGuardInstrumentation.ActivitySource.StartActivity(name);
        activity?.SetTag("orionguard.validator_type", typeof(T).Name);
        return activity;
    }

    private static void RecordMetrics(GuardResult result, TimeSpan elapsed, Activity? activity)
    {
        OrionGuardInstrumentation.ValidationDuration.Record(elapsed.TotalMilliseconds);
        OrionGuardInstrumentation.ValidationsTotal.Add(1);

        if (result.IsInvalid)
        {
            OrionGuardInstrumentation.ValidationFailures.Add(1);
            activity?.SetTag("orionguard.validation_result", "failed");
            activity?.SetTag("orionguard.error_count", result.Errors.Count);
        }
        else
        {
            activity?.SetTag("orionguard.validation_result", "success");
        }
    }
}
