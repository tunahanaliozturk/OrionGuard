# OrionGuard.OpenTelemetry

Metrics and tracing for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). It wraps your registered `IValidator<T>` services and the domain-event dispatcher with `System.Diagnostics` instrumentation that OpenTelemetry can collect and export.

## Install

```bash
dotnet add package OrionGuard.OpenTelemetry
```

The package depends only on `OpenTelemetry.Api`. To collect and export telemetry, add the OpenTelemetry SDK and an exporter, for example `OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.OpenTelemetryProtocol`.

## Quick start

```csharp
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddValidator<CreateUserRequest, CreateUserValidator>();
builder.Services.AddOrionGuardOpenTelemetry(); // call after all validators are registered

builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(OrionGuardInstrumentation.MeterName).AddOtlpExporter())
    .WithTracing(t => t.AddSource(OrionGuardInstrumentation.ActivitySourceName).AddOtlpExporter());
```

There is no `AddOrionGuardInstrumentation()` builder extension. Subscribe to the meter and activity source by name as shown.

## Validator instrumentation

`AddOrionGuardOpenTelemetry()` replaces every closed, non-keyed `IValidator<T>` registration already in the service collection with an `InstrumentedValidator<T>` that wraps the original and keeps its lifetime. Validators registered after the call are not instrumented. Neither is validation that does not go through a DI-resolved `IValidator<T>`, such as `Guard.Against`, `Ensure`, or `AttributeValidator`.

Meter and ActivitySource name: `Moongazing.OrionGuard` (`OrionGuardInstrumentation.MeterName`, `OrionGuardInstrumentation.ActivitySourceName`). The instrumentation version is the package version.

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `orionguard.validations.total` | `Counter<long>` | none | none |
| `orionguard.validations.failures` | `Counter<long>` | none | none |
| `orionguard.validations.duration_ms` | `Histogram<double>` | ms | none |

Spans are named `OrionGuard.Validate` or `OrionGuard.ValidateAsync`, depending on the method called. They carry these tags:

- `orionguard.validator_type`: the validated model type's name (`typeof(T).Name`)
- `orionguard.validation_result`: `success` or `failed`
- `orionguard.error_count`: set only when validation fails

Spans are `Internal` and start under the current `Activity`, for example an ASP.NET Core request span.

`InstrumentedValidator<T>` implements every `IValidator<T>` overload, including `Validate(T, ValidationContext)` and `ValidateAsync(T, ValidationContext, CancellationToken)`, and passes the `ValidationContext` on to the wrapped validator.

Open-generic registrations (`AddTransient(typeof(IValidator<>), typeof(MyValidator<>))`) and keyed registrations are left unchanged and are not instrumented.

## Domain-event instrumentation

```csharp
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.OpenTelemetry.DomainEvents;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddOrionGuardDomainEvents();
builder.Services.WithOpenTelemetryDomainEvents();

builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(OrionGuardDomainEventTelemetry.MeterName))
    .WithTracing(t => t.AddSource(OrionGuardDomainEventTelemetry.ActivitySourceName));
```

`WithOpenTelemetryDomainEvents()` wraps the last registered `IDomainEventDispatcher` in an `InstrumentedDomainEventDispatcher` with the same lifetime. It throws `InvalidOperationException` if no dispatcher is registered, and a second call does nothing. With the MediatR bridge, the order must be `AddOrionGuardDomainEvents()`, then `AddOrionGuardMediatRDomainEvents()`, then `WithOpenTelemetryDomainEvents()`.

Meter and ActivitySource name: `Moongazing.OrionGuard.DomainEvents` (`OrionGuardDomainEventTelemetry.MeterName`, `OrionGuardDomainEventTelemetry.ActivitySourceName`).

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `orionguard.domain_events.dispatched` | `Counter<long>` | events | `event_type` |
| `orionguard.domain_events.failed` | `Counter<long>` | events | `event_type` |
| `orionguard.domain_events.duration` | `Histogram<double>` | ms | `event_type` |

`event_type` is the event's short CLR type name. Each event gets its own `Internal` span named `DomainEvent.Dispatch <EventTypeName>`, including events dispatched as a batch. Each span carries these tags:

- `orionguard.event.id`
- `orionguard.event.type`: the full type name
- `orionguard.event.occurred_on`: ISO 8601 round-trip format

On failure, the span status is `Error` with the exception message, and an `exception` event with `exception.type` and `exception.message` is added. The exception is then rethrown.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- `OpenTelemetry.Api` 1.19.0 or later

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR) (MediatR domain-event bridge), [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
