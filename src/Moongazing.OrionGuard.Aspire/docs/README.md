# OrionGuard.Aspire

One line in your Aspire ServiceDefaults project puts every OrionGuard meter, activity source and health check into the dashboard — no per-service wiring, no names to copy.

```bash
dotnet add package OrionGuard.Aspire
```

```csharp
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.Aspire;

public static class OrionGuardServiceDefaults
{
    // In the ServiceDefaults project's AddServiceDefaults(), next to
    // ConfigureOpenTelemetry() and AddDefaultHealthChecks().
    public static TBuilder AddOrionGuard<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddOrionGuardDefaults();
        return builder;
    }
}
```

Validation, domain-event and EF Core outbox telemetry now show up on the Aspire dashboard's Metrics and Traces pages for every service that uses these defaults, and `/health` gains the OrionGuard checks the service's packages support.

The package depends on `OrionGuard.OpenTelemetry`, `OpenTelemetry.Api.ProviderBuilderExtensions` and the health-check and hosting abstractions. It depends on no Aspire package — the dashboard reads the OTLP data your app already exports — and on neither ASP.NET Core nor EF Core: the checks from those packages are picked up only when your app references them.

## Turn the instruments on where they are emitted

`AddOrionGuardDefaults()` subscribes to OrionGuard's telemetry; it does not create it. Validator instrumentation is a decorator, so it still has to be applied after the validators exist:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.OpenTelemetry;

public sealed record CreateOrder(string CustomerId, int Quantity);

public sealed class CreateOrderValidator : AbstractValidator<CreateOrder>
{
    public CreateOrderValidator()
    {
        RuleFor(x => !string.IsNullOrEmpty(x.CustomerId), "CustomerId is required.", "CustomerId");
        RuleFor(x => x.Quantity > 0, "Quantity must be positive.", "Quantity");
    }
}

public static class OrderServiceSetup
{
    public static void Add(IServiceCollection services)
    {
        services.AddOrionGuard();
        services.AddValidator<CreateOrder, CreateOrderValidator>();
        services.AddOrionGuardOpenTelemetry(); // once, after every validator is registered
    }
}
```

`WithOpenTelemetryDomainEvents()` is the same story for the `IDomainEventDispatcher`. The EF Core outbox needs no extra call — it records its metrics and spans on its own.

## What gets subscribed

`AddOrionGuardDefaults()` subscribes the app's meter provider to `Moongazing.OrionGuard` and `Moongazing.OrionGuard.*`, and the tracer provider to the activity sources of the same names — the wildcard is what covers meters owned by packages this one does not reference.

| Name | Meter | ActivitySource | Emitted by |
| --- | --- | --- | --- |
| `Moongazing.OrionGuard` | yes | yes | Validators wrapped by `AddOrionGuardOpenTelemetry()` |
| `Moongazing.OrionGuard.DomainEvents` | yes | yes | The dispatcher wrapped by `WithOpenTelemetryDomainEvents()`, and the outbox dispatcher's `Outbox.Dispatch` spans |
| `Moongazing.OrionGuard.Outbox.Dispatcher` | yes | no | The EF Core outbox dispatcher |
| `Moongazing.OrionGuard.Outbox.Archival` | yes | no | Outbox archival, once `UseOutboxArchival()` is on |

The subscription goes through `ConfigureOpenTelemetryMeterProvider` and `ConfigureOpenTelemetryTracerProvider`, so it extends the providers your app builds with `AddOpenTelemetry()` and never creates one. Exporters stay yours — the ServiceDefaults template's `ConfigureOpenTelemetry()` adds the OTLP exporter the Aspire dashboard reads. The order of `AddOrionGuardDefaults()` and `AddOpenTelemetry()` does not matter.

Instrument names and tags are listed in the [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry) and [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) READMEs.

## Health checks

| Name | Check | Added when | Default | Tags |
| --- | --- | --- | --- | --- |
| `orionguard` | `OrionGuardHealthCheck` | The app references `OrionGuard.AspNetCore` | on | `validation`, `orionguard` |
| `orionguard-outbox-archival` | `OutboxArchivalHealthCheck` | The app references `OrionGuard.EntityFrameworkCore` and enables archival | off | `outbox`, `orionguard` |

`orionguard` reports `Degraded` when `AddOrionGuard()` was never called and `Healthy` otherwise; with the default endpoint options `Degraded` still answers 200, so a missing registration does not pull a service out of rotation.

`orionguard-outbox-archival` is off by default on purpose: it reports on a background maintenance job, and once it counts towards readiness, archival falling behind takes the service out of rotation. Turn it on with `EnableOutboxArchivalHealthCheck`. Thresholds you leave unset scale with the archival `PollingInterval` — two intervals for `Degraded`, three for `Unhealthy`, never below 5 and 15 minutes — so the check does not flap between hourly batches. Services that do not enable archival skip it, which is why the option can be set once in ServiceDefaults.

Both checks are added when the health-check options are first read, after the app has registered everything, so the order of `AddOrionGuardDefaults()` and `AddOrionGuardEfCore(...)` does not matter either. A check you already registered under the same name is kept rather than added twice, and the names are available as `OrionGuardAspireExtensions.ValidationHealthCheckName` and `OrionGuardAspireExtensions.OutboxArchivalHealthCheckName`.

`AddOrionGuardDefaults()` calls `AddHealthChecks()`, so `HealthCheckService` exists even in a worker service with no ServiceDefaults health endpoints.

## Options

| Option | Default | Effect |
| --- | --- | --- |
| `DisableValidationHealthCheck` | `false` | `true` skips the `orionguard` check |
| `EnableOutboxArchivalHealthCheck` | `false` | `true` adds `orionguard-outbox-archival` in services that enable archival |

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.Aspire;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

public static class OrionGuardAspireOptionsSample
{
    public static void Configure(IHostApplicationBuilder builder)
    {
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
    }
}
```

Calling `AddOrionGuardDefaults()` a second time registers nothing new, but its options callback still runs, after the earlier ones — so a service can override what its ServiceDefaults project configured.

## What this does not do

- **It does not produce telemetry.** It only subscribes. Without `AddOrionGuardOpenTelemetry()` (validators) or `WithOpenTelemetryDomainEvents()` (the dispatcher), those meters and sources emit nothing and the dashboard pages stay empty; the outbox is the exception, since it instruments itself.
- **It does not set up OpenTelemetry.** With no `AddOpenTelemetry()` in the app, the call collects nothing at all. It also adds no exporter — that is the ServiceDefaults template's job.
- **It cannot see validators registered after `AddOrionGuardOpenTelemetry()`.** That is a decorator over what is already in the collection, so registration order matters even though this package's own order does not.
- **It reads no Aspire API.** There is no dependency on an Aspire package and no resource, connection string or configuration is discovered; it is an OpenTelemetry and health-check wiring helper that happens to be exactly what an Aspire app needs.
- **Neither check is tagged `live`**, so `/alive` is unaffected — and `orionguard` only reports whether the validation stack is wired up, not whether any validator works.

## Targets

`net8.0`, `net9.0`, `net10.0`; `OpenTelemetry.Api.ProviderBuilderExtensions` 1.x, plus an app that sets OpenTelemetry up with `OpenTelemetry.Extensions.Hosting` (`AddOpenTelemetry()`), as the Aspire ServiceDefaults template does. `AddOrionGuardDefaults` extends `IHostApplicationBuilder` and returns the builder type you called it on, like `AddServiceDefaults`.

## With the rest of OrionGuard

[OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry) (the validation and domain-event instrumentation) · [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) (the validation health check) · [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (the outbox and its archival health check)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- [Aspire service defaults](https://aspire.dev/get-started/csharp-service-defaults/)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
