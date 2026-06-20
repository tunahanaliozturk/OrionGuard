using System.Reflection;

namespace Moongazing.OrionGuard.OpenTelemetry;

/// <summary>
/// Derives the diagnostics instrumentation version (used for <see cref="System.Diagnostics.Metrics.Meter"/>
/// and <see cref="System.Diagnostics.ActivitySource"/>) from THIS assembly's own version so the meter
/// version can never drift away from the package version it ships in.
/// </summary>
internal static class MeterVersion
{
    /// <summary>The resolved version string (the package version, build-metadata stripped).</summary>
    public static string Value { get; } = Resolve();

    private static string Resolve()
    {
        var asm = typeof(MeterVersion).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
