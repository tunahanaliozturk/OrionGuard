using System.Diagnostics;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.OpenTelemetry;

/// <summary>
/// Decorator that wraps an <see cref="IValidator{T}"/> with OpenTelemetry instrumentation,
/// recording validation metrics (count, failures, duration) and distributed tracing spans.
/// </summary>
public sealed class InstrumentedValidator<T> : IValidator<T>
{
    private readonly IValidator<T> _inner;

    public InstrumentedValidator(IValidator<T> inner)
    {
        _inner = inner;
    }

    public GuardResult Validate(T value)
    {
        using var activity = OrionGuardInstrumentation.ActivitySource.StartActivity("OrionGuard.Validate");
        activity?.SetTag("orionguard.validator_type", typeof(T).Name);

        var startTimestamp = Stopwatch.GetTimestamp();
        var result = _inner.Validate(value);
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

        RecordMetrics(result, elapsed, activity);
        return result;
    }

    public async Task<GuardResult> ValidateAsync(T value, CancellationToken cancellationToken = default)
    {
        using var activity = OrionGuardInstrumentation.ActivitySource.StartActivity("OrionGuard.ValidateAsync");
        activity?.SetTag("orionguard.validator_type", typeof(T).Name);

        var startTimestamp = Stopwatch.GetTimestamp();
        var result = await _inner.ValidateAsync(value, cancellationToken).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);

        RecordMetrics(result, elapsed, activity);
        return result;
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
