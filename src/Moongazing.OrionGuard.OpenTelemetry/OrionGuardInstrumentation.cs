using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Moongazing.OrionGuard.OpenTelemetry;

/// <summary>
/// Central instrumentation class for OrionGuard validation telemetry.
/// </summary>
public static class OrionGuardInstrumentation
{
    public const string ActivitySourceName = "Moongazing.OrionGuard";
    public const string MeterName = "Moongazing.OrionGuard";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, "6.0.0");
    internal static readonly Meter Meter = new(MeterName, "6.0.0");

    internal static readonly Counter<long> ValidationsTotal = Meter.CreateCounter<long>(
        "orionguard.validations.total",
        description: "Total number of validations performed");

    internal static readonly Counter<long> ValidationFailures = Meter.CreateCounter<long>(
        "orionguard.validations.failures",
        description: "Total number of failed validations");

    internal static readonly Histogram<double> ValidationDuration = Meter.CreateHistogram<double>(
        "orionguard.validations.duration_ms",
        unit: "ms",
        description: "Duration of validation operations in milliseconds");
}
