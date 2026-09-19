# OrionGuard.Aspire

Aspire integration for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). One call, `builder.AddOrionGuardDefaults()`, subscribes the app's OpenTelemetry pipeline to every OrionGuard meter and activity source, so validation, domain-event, and EF Core outbox telemetry shows up in the Aspire dashboard's Metrics and Traces pages. It also registers the OrionGuard health checks for the packages the app uses.

## Install

```bash
dotnet add package OrionGuard.Aspire
```

The package depends on `OrionGuard.OpenTelemetry`, `OpenTelemetry.Api.ProviderBuilderExtensions`, `Microsoft.Extensions.Diagnostics.HealthChecks`, and `Microsoft.Extensions.Hosting.Abstractions`. It does not depend on any Aspire package, because the dashboard reads the OTLP data your app already exports. It does not depend on ASP.NET Core or EF Core either: the health checks from `OrionGuard.AspNetCore` and `OrionGuard.EntityFrameworkCore` are picked up when your app references those packages.

## Quick start

Add one line to `AddServiceDefaults()` in your Aspire ServiceDefaults project (`Extensions.cs` from the `aspire-servicedefaults` template):

```csharp
using Moongazing.OrionGuard.Aspire;

public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
{
    builder.ConfigureOpenTelemetry();
    builder.AddDefaultHealthChecks();
    builder.AddOrionGuardDefaults();

    builder.Services.AddServiceDiscovery();
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler();
        http.AddServiceDiscovery();
    });

    return builder;
}
```

Then, in each service, turn on the instrumentation for your validators after registering them:

```csharp
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOrionGuard();
builder.Services.AddValidator<CreateOrder, CreateOrderValidator>();
builder.Services.AddOrionGuardOpenTelemetry(); // once, after every validator is registered

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapPost("/orders", async (CreateOrder order, IValidator<CreateOrder> validator) =>
{
    var result = await validator.ValidateAsync(order);
    return result.IsValid ? Results.Ok() : Results.BadRequest(result.Errors);
});

app.Run();

public sealed record CreateOrder(string CustomerId, int Quantity);

public sealed class CreateOrderValidator : AbstractValidator<CreateOrder>
{
    public CreateOrderValidator()
    {
        RuleFor(x => !string.IsNullOrEmpty(x.CustomerId), "CustomerId is required.", "CustomerId");
        RuleFor(x => x.Quantity > 0, "Quantity must be positive.", "Quantity");
    }
}
```

`AddOrionGuardDefaults()` only subscribes to OrionGuard's telemetry. The instruments themselves are turned on where they are emitted: `AddOrionGuardOpenTelemetry()` wraps the `IValidator<T>` registrations that already exist, and `WithOpenTelemetryDomainEvents()` wraps the registered `IDomainEventDispatcher`, which is why both are called after the registrations they wrap. The EF Core outbox records its metrics and spans without any extra call.

## Telemetry

`AddOrionGuardDefaults()` subscribes the app's meter provider to `Moongazing.OrionGuard` and `Moongazing.OrionGuard.*`, and the app's tracer provider to the activity sources with the same names. The names come from `OrionGuardInstrumentation.MeterName` and `OrionGuardInstrumentation.ActivitySourceName`, and the wildcard covers the meters owned by packages this one does not reference.

| Name | Meter | ActivitySource | Emitted by |
| --- | --- | --- | --- |
| `Moongazing.OrionGuard` | yes | yes | Validators wrapped by `AddOrionGuardOpenTelemetry()` (`OrionGuard.OpenTelemetry`) |
| `Moongazing.OrionGuard.DomainEvents` | yes | yes | The dispatcher wrapped by `WithOpenTelemetryDomainEvents()`, and the `Outbox.Dispatch` spans of the EF Core outbox dispatcher |
| `Moongazing.OrionGuard.Outbox.Dispatcher` | yes | no | The EF Core outbox dispatcher (`OutboxDispatcherDiagnostics.MeterName`) |
| `Moongazing.OrionGuard.Outbox.Archival` | yes | no | Outbox archival, when enabled with `UseOutboxArchival()` (`OutboxArchivalDiagnostics.MeterName`) |

The instrument names and tags are listed in the [OrionGuard.OpenTelemetry README](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard.OpenTelemetry/docs/README.md) and the [OrionGuard.EntityFrameworkCore README](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard.EntityFrameworkCore/docs/README.md).

The subscription is added through `ConfigureOpenTelemetryMeterProvider` and `ConfigureOpenTelemetryTracerProvider`, so it extends the providers your app creates with `AddOpenTelemetry()` and never creates one itself. The exporters are yours too: the ServiceDefaults template's `ConfigureOpenTelemetry()` adds the OTLP exporter that the Aspire dashboard reads. Without `AddOpenTelemetry()` in the app, the call collects nothing. The order of `AddOrionGuardDefaults()` and `AddOpenTelemetry()` does not matter.

## Health checks

| Name | Check | Added when | Default | Tags |
| --- | --- | --- | --- | --- |
| `orionguard` | `OrionGuardHealthCheck` | The app references `OrionGuard.AspNetCore` | on | `validation`, `orionguard` |
| `orionguard-outbox-archival` | `OutboxArchivalHealthCheck` | The app references `OrionGuard.EntityFrameworkCore` and enables archival with `UseOutboxArchival()` | off | `outbox`, `orionguard` |

- `orionguard` reports `Degraded` when `AddOrionGuard()` was not called, and `Healthy` otherwise. With the default health endpoint options `Degraded` still answers `200`, so a missing `AddOrionGuard()` does not take a service out of rotation. Turn the check off with `DisableValidationHealthCheck`.
- `orionguard-outbox-archival` reports `Degraded` before the archival worker's first batch or once the last batch is older than `DegradedAfter`, and `Unhealthy` once it is older than `UnhealthyAfter`. Turn it on with `EnableOutboxArchivalHealthCheck`. Thresholds you leave unset scale with the archival `PollingInterval` - two intervals for `Degraded`, three for `Unhealthy`, never below 5 and 15 minutes - so the default 1-hour interval gives 2 and 3 hours and the check does not flap between batches. Set them on an `OutboxArchivalHealthCheckOptions` singleton only to tighten them, as the [OrionGuard.EntityFrameworkCore README](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard.EntityFrameworkCore/docs/README.md) shows. It is off by default because it reports on a background maintenance job: once it is in the readiness report, archival falling behind takes the service out of rotation. Services that do not enable archival skip the check, so the option can be set once in ServiceDefaults.
- The ServiceDefaults template's `MapDefaultEndpoints()` runs every check on `/health`, so both count toward readiness. Neither is tagged `live`, so `/alive` is unaffected.
- The checks are added when the health check options are first read, after the app has registered everything. The order of `AddOrionGuardDefaults()` and `AddOrionGuardEfCore(...)` therefore does not matter.
- A check the app already registered under the same name is kept and not added again, so calling `AddHealthChecks().AddOrionGuardCheck()` as well is harmless.
- The names are available as `OrionGuardAspireExtensions.ValidationHealthCheckName` and `OrionGuardAspireExtensions.OutboxArchivalHealthCheckName`.

`AddOrionGuardDefaults()` calls `AddHealthChecks()`, so `HealthCheckService` is registered even in a worker service without the ServiceDefaults health checks.

## Options

Pass a callback to turn the health checks on or off:

| Option | Default | Effect |
| --- | --- | --- |
| `DisableValidationHealthCheck` | `false` | `true` skips the `orionguard` check |
| `EnableOutboxArchivalHealthCheck` | `false` | `true` adds the `orionguard-outbox-archival` check in services that enable archival |

```csharp
using Moongazing.OrionGuard.Aspire;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

builder.AddOrionGuardDefaults(options =>
{
    options.DisableValidationHealthCheck = true;
    options.EnableOutboxArchivalHealthCheck = true;
});

// Archival polls once an hour by default, so allow more than an hour between batches.
builder.Services.AddSingleton(new OutboxArchivalHealthCheckOptions
{
    DegradedAfter = TimeSpan.FromHours(2),
    UnhealthyAfter = TimeSpan.FromHours(3),
});
```

## Calling it more than once

A second call registers nothing new. Its options callback is still applied, after the earlier ones, so a service can change a check that its ServiceDefaults project configured:

```csharp
using Moongazing.OrionGuard.Aspire;

builder.AddServiceDefaults(); // calls builder.AddOrionGuardDefaults()
builder.AddOrionGuardDefaults(options => options.DisableValidationHealthCheck = true);
```

## Targets

- `net8.0`, `net9.0`, `net10.0`
- `OpenTelemetry.Api.ProviderBuilderExtensions` 1.19.0 or later, and an app that sets up OpenTelemetry with `OpenTelemetry.Extensions.Hosting` (`AddOpenTelemetry()`), as the Aspire ServiceDefaults template does
- `AddOrionGuardDefaults` extends `IHostApplicationBuilder` and returns the builder type you call it on, like `AddServiceDefaults`

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- [Aspire service defaults](https://aspire.dev/get-started/csharp-service-defaults/)
- Related packages: [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry) (the validation and domain-event instrumentation), [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) (the validation health check), [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (the outbox and its archival health check)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
