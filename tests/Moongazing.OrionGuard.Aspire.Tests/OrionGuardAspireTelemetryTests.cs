using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Moongazing.OrionGuard.OpenTelemetry;
using Moongazing.OrionGuard.OpenTelemetry.DomainEvents;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Moongazing.OrionGuard.Aspire.Tests;

/// <summary>
/// Builds a host the way the Aspire ServiceDefaults template does (AddOpenTelemetry with metrics and tracing),
/// with in-memory exporters in place of OTLP, and checks which OrionGuard telemetry reaches them. OrionGuard's
/// instruments are process-wide statics, so the tests assert that telemetry is present, never how often.
/// </summary>
public sealed class OrionGuardAspireTelemetryTests
{
    public sealed record SignUp(string Email);

    public sealed class SignUpValidator : AbstractValidator<SignUp>
    {
        public SignUpValidator()
        {
            RuleFor(x => !string.IsNullOrEmpty(x.Email), "Email is required.", "Email");
        }
    }

    public sealed record OrderPlaced(int OrderId) : DomainEventBase;

    public sealed class OrderPlacedHandler : IDomainEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced @event, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Where AddOrionGuardDefaults is called relative to the app's AddOpenTelemetry.</summary>
    public enum Subscription
    {
        None,
        BeforeAddOpenTelemetry,

        // The ServiceDefaults template's order: ConfigureOpenTelemetry() runs first.
        AfterAddOpenTelemetry,
    }

    private sealed class TelemetryHost : IDisposable
    {
        private readonly IHost host;

        public TelemetryHost(Subscription subscription, Action<IServiceCollection> registerServices)
        {
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            registerServices(builder.Services);

            if (subscription == Subscription.BeforeAddOpenTelemetry)
            {
                builder.AddOrionGuardDefaults();
            }

            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics.AddInMemoryExporter(Metrics))
                .WithTracing(tracing => tracing.AddInMemoryExporter(Activities));

            if (subscription == Subscription.AfterAddOpenTelemetry)
            {
                builder.AddOrionGuardDefaults();
            }

            host = builder.Build();

            // Resolving the providers creates their listeners, as starting the host would.
            host.Services.GetRequiredService<MeterProvider>();
            host.Services.GetRequiredService<TracerProvider>();
        }

        public List<Metric> Metrics { get; } = new();

        public List<Activity> Activities { get; } = new();

        public IServiceProvider Services => host.Services;

        public IReadOnlyCollection<string> ExportedMetricNames(string meterName)
        {
            host.Services.GetRequiredService<MeterProvider>().ForceFlush();
            return Metrics.Where(m => m.MeterName == meterName).Select(m => m.Name).ToHashSet();
        }

        public void Dispose() => host.Dispose();
    }

    private static void RegisterInstrumentedValidator(IServiceCollection services)
    {
        services.AddOrionGuard();
        services.AddValidator<SignUp, SignUpValidator>();
        services.AddOrionGuardOpenTelemetry();
    }

    private static void RegisterInstrumentedDomainEvents(IServiceCollection services)
    {
        services.AddOrionGuardDomainEvents();
        services.AddScoped<IDomainEventHandler<OrderPlaced>, OrderPlacedHandler>();
        services.WithOpenTelemetryDomainEvents();
    }

    [Fact]
    public async Task ValidationTelemetry_ShouldReachTheExporters_WhenAnInstrumentedValidatorRuns()
    {
        using var host = new TelemetryHost(Subscription.AfterAddOpenTelemetry, RegisterInstrumentedValidator);

        var result = await host.Services.GetRequiredService<IValidator<SignUp>>().ValidateAsync(new SignUp(""));

        Assert.True(result.IsInvalid);
        var metricNames = host.ExportedMetricNames(OrionGuardInstrumentation.MeterName);
        Assert.Contains("orionguard.validations.total", metricNames);
        Assert.Contains("orionguard.validations.failures", metricNames);
        Assert.Contains("orionguard.validations.duration_ms", metricNames);
        var span = host.Activities.FirstOrDefault(a =>
            a.Source.Name == OrionGuardInstrumentation.ActivitySourceName
            && a.GetTagItem("orionguard.validator_type") as string == nameof(SignUp));
        Assert.NotNull(span);
        Assert.Equal("OrionGuard.ValidateAsync", span.DisplayName);
        Assert.Equal("failed", span.GetTagItem("orionguard.validation_result"));
    }

    [Fact]
    public async Task DomainEventTelemetry_ShouldReachTheExporters_WhenAnInstrumentedDispatcherRuns()
    {
        using var host = new TelemetryHost(Subscription.BeforeAddOpenTelemetry, RegisterInstrumentedDomainEvents);
        var orderPlaced = new OrderPlaced(42);

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().DispatchAsync(orderPlaced);
        }

        Assert.Contains("orionguard.domain_events.dispatched", host.ExportedMetricNames(OrionGuardDomainEventTelemetry.MeterName));
        Assert.Contains(host.Activities, a =>
            a.Source.Name == OrionGuardDomainEventTelemetry.ActivitySourceName
            && a.GetTagItem("orionguard.event.id") as string == orderPlaced.EventId.ToString("D"));
    }

    [Theory]
    [InlineData(Subscription.BeforeAddOpenTelemetry)]
    [InlineData(Subscription.AfterAddOpenTelemetry)]
    public void OutboxTelemetry_ShouldReachTheMetricExporter_WhenTheDispatcherAndArchivalRecord(Subscription subscription)
    {
        using var host = new TelemetryHost(subscription, _ => { });

        // The public recording entry points the EF Core outbox dispatcher and archive sinks call.
        OutboxDispatcherDiagnostics.RecordQueueLag(12.5);
        OutboxArchivalDiagnostics.RecordBytes(1024, "aspire-tests");

        Assert.Contains("orionguard.outbox.dispatcher.queue_lag", host.ExportedMetricNames(OutboxDispatcherDiagnostics.MeterName));
        Assert.Contains("orionguard.outbox.archive.bytes_written", host.ExportedMetricNames(OutboxArchivalDiagnostics.MeterName));
    }

    [Fact]
    public async Task OrionGuardTelemetry_ShouldNotReachTheExporters_WithoutAddOrionGuardDefaults()
    {
        // Control for the tests above: the instrumentation is on, only the subscription is missing.
        using var host = new TelemetryHost(Subscription.None, services =>
        {
            RegisterInstrumentedValidator(services);
            RegisterInstrumentedDomainEvents(services);
        });

        await host.Services.GetRequiredService<IValidator<SignUp>>().ValidateAsync(new SignUp(""));
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().DispatchAsync(new OrderPlaced(7));
        }
        OutboxDispatcherDiagnostics.RecordQueueLag(12.5);
        OutboxArchivalDiagnostics.RecordBytes(1024, "aspire-tests");

        Assert.Empty(host.ExportedMetricNames(OrionGuardInstrumentation.MeterName));
        Assert.Empty(host.ExportedMetricNames(OrionGuardDomainEventTelemetry.MeterName));
        Assert.Empty(host.ExportedMetricNames(OutboxDispatcherDiagnostics.MeterName));
        Assert.Empty(host.ExportedMetricNames(OutboxArchivalDiagnostics.MeterName));
        Assert.DoesNotContain(host.Activities, a => a.Source.Name.StartsWith(OrionGuardInstrumentation.ActivitySourceName, StringComparison.Ordinal));
    }
}
