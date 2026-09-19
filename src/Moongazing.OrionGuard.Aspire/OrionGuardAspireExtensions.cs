using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moongazing.OrionGuard.OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Moongazing.OrionGuard.Aspire;

/// <summary>
/// The single entry point that wires OrionGuard into an Aspire service: its telemetry for the Aspire dashboard
/// and its health checks.
/// </summary>
public static class OrionGuardAspireExtensions
{
    /// <summary>
    /// Name of the validation infrastructure health check. It is the name <c>AddOrionGuardCheck()</c> uses by default,
    /// so an app that already calls <c>AddOrionGuardCheck()</c> keeps its own registration.
    /// </summary>
    public const string ValidationHealthCheckName = "orionguard";

    /// <summary>Name of the outbox archival health check.</summary>
    public const string OutboxArchivalHealthCheckName = "orionguard-outbox-archival";

    // Every OrionGuard meter and activity source is named under the validation instrumentation's name:
    // Moongazing.OrionGuard (validation), .DomainEvents (domain events and outbox dispatch spans), and
    // .Outbox.Dispatcher / .Outbox.Archival (EF Core outbox). The wildcard covers the names owned by packages
    // this assembly does not reference, and any added later, without a dependency on those packages.
    private static readonly string[] MeterNames =
    {
        OrionGuardInstrumentation.MeterName,
        OrionGuardInstrumentation.MeterName + ".*",
    };

    private static readonly string[] ActivitySourceNames =
    {
        OrionGuardInstrumentation.ActivitySourceName,
        OrionGuardInstrumentation.ActivitySourceName + ".*",
    };

    // Resolved by name so this package does not force ASP.NET Core or EF Core onto apps that use neither.
    // Type.GetType returns null when the owning package is not referenced, and the check is then skipped.
    // The Aspire test project references both packages and pins every name to the real type.
    private const string ValidationHealthCheckTypeName =
        "Moongazing.OrionGuard.AspNetCore.HealthChecks.OrionGuardHealthCheck, Moongazing.OrionGuard.AspNetCore";

    private const string OutboxArchivalHealthCheckTypeName =
        "Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival.OutboxArchivalHealthCheck, Moongazing.OrionGuard.EntityFrameworkCore";

    private const string OutboxArchivalStateTypeName =
        "Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival.OutboxArchivalState, Moongazing.OrionGuard.EntityFrameworkCore";

    /// <summary>
    /// Adds OrionGuard's telemetry and health checks to an Aspire service. Call it from the ServiceDefaults
    /// project's <c>AddServiceDefaults()</c> or from the service's <c>Program.cs</c>.
    /// </summary>
    /// <typeparam name="TBuilder">The host builder type, for example <c>WebApplicationBuilder</c>.</typeparam>
    /// <param name="builder">The host builder.</param>
    /// <param name="configure">Optional callback that turns individual health checks on or off.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Telemetry: subscribes the app's OpenTelemetry meter and tracer providers to every OrionGuard meter and
    /// activity source. The app still creates the providers and the exporters itself (the ServiceDefaults
    /// template's <c>ConfigureOpenTelemetry()</c> does both); this call adds nothing if there are none. Validator
    /// and domain-event instrumentation still has to be turned on with <c>AddOrionGuardOpenTelemetry()</c> and
    /// <c>WithOpenTelemetryDomainEvents()</c>, because those decorate registrations that must already exist.
    /// </para>
    /// <para>
    /// Health checks: adds the validation infrastructure check when OrionGuard.AspNetCore is referenced, and, when
    /// <see cref="OrionGuardAspireOptions.EnableOutboxArchivalHealthCheck"/> is set, the outbox archival check in
    /// services that enable outbox archival. Both are decided when the health check options are first read, so the
    /// order of this call and <c>AddOrionGuardEfCore</c> does not matter, and a check the app already registered
    /// under the same name is not added again.
    /// </para>
    /// <para>
    /// Calling this method more than once registers everything once. Each call's <paramref name="configure"/> is
    /// still applied, in call order.
    /// </para>
    /// </remarks>
    public static TBuilder AddOrionGuardDefaults<TBuilder>(
        this TBuilder builder,
        Action<OrionGuardAspireOptions>? configure = null)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        // Options are applied on every call, so ServiceDefaults can call this and the service can still
        // change a check with a second call.
        var options = services.AddOptions<OrionGuardAspireOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        if (services.Any(d => d.ServiceType == typeof(OrionGuardAspireMarker)))
        {
            return builder;
        }

        services.AddSingleton<OrionGuardAspireMarker>();

        // The Configure* extensions only add to providers the app creates; they never create one, so an app
        // without OpenTelemetry does not start collecting anything because of this call.
        services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(MeterNames));
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(ActivitySourceNames));

        // PostConfigure runs after every AddCheck the app made (those are Configure actions) and after the
        // container is built, when all registrations, including a later AddOrionGuardEfCore, are known.
        services.AddHealthChecks();
        services.AddOptions<HealthCheckServiceOptions>()
            .PostConfigure<IServiceProvider, IOptions<OrionGuardAspireOptions>>(AddAvailableHealthChecks);

        return builder;
    }

    private static void AddAvailableHealthChecks(
        HealthCheckServiceOptions healthChecks,
        IServiceProvider services,
        IOptions<OrionGuardAspireOptions> aspireOptions)
    {
        var options = aspireOptions.Value;

        if (!options.DisableValidationHealthCheck)
        {
            // Same tags as AddOrionGuardCheck(), so health endpoint filters see no difference.
            TryAddHealthCheck(
                healthChecks,
                ValidationHealthCheckName,
                Type.GetType(ValidationHealthCheckTypeName),
                "validation",
                "orionguard");
        }

        // The archival check needs OutboxArchivalState, which only UseOutboxArchival() registers. Without it the
        // check could not be constructed and would break every health report.
        if (options.EnableOutboxArchivalHealthCheck
            && Type.GetType(OutboxArchivalStateTypeName) is { } stateType
            && services.GetService(stateType) is not null)
        {
            TryAddHealthCheck(
                healthChecks,
                OutboxArchivalHealthCheckName,
                Type.GetType(OutboxArchivalHealthCheckTypeName),
                "outbox",
                "orionguard");
        }
    }

    private static void TryAddHealthCheck(
        HealthCheckServiceOptions healthChecks,
        string name,
        Type? healthCheckType,
        params string[] tags)
    {
        // HealthCheckService rejects duplicate names case-insensitively, so compare the same way.
        if (healthCheckType is null
            || healthChecks.Registrations.Any(r => string.Equals(r.Name, name,StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // Same activation as AddCheck<T>(): a registered instance is used, otherwise one is built for each run.
        healthChecks.Registrations.Add(new HealthCheckRegistration(
            name,
            sp => (IHealthCheck)ActivatorUtilities.GetServiceOrCreateInstance(sp, healthCheckType),
            failureStatus: null,
            tags));
    }

    /// <summary>Registered by the first call so later calls do not register anything twice.</summary>
    private sealed class OrionGuardAspireMarker
    {
    }
}
