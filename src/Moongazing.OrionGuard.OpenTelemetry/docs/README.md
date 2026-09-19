# OrionGuard.OpenTelemetry

Answers "how often does validation fail, and what does it cost" by wrapping your registered validators and the domain-event dispatcher in `System.Diagnostics` instrumentation any OpenTelemetry SDK can collect.

```bash
dotnet add package OrionGuard.OpenTelemetry
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

public sealed record CreateUserRequest(string Email);

public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator() =>
        RuleFor(x => x.Email, nameof(CreateUserRequest.Email), p => p.NotEmpty().Email());
}

public static class TelemetrySetup
{
    public static void Add(IServiceCollection services)
    {
        services.AddValidator<CreateUserRequest, CreateUserValidator>();
        services.AddOrionGuardOpenTelemetry(); // after every validator is registered

        services.AddOpenTelemetry()
            .WithMetrics(m => m.AddMeter(OrionGuardInstrumentation.MeterName))
            .WithTracing(t => t.AddSource(OrionGuardInstrumentation.ActivitySourceName));
    }
}
```

Every validation now emits a count, a failure count and a duration on the meter `Moongazing.OrionGuard`, plus an `Internal` span under whatever activity is current — an ASP.NET Core request span, for instance. The package itself depends only on `OpenTelemetry.Api`; add the SDK and an exporter (`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`) to get the data out.

There is no `AddOrionGuardInstrumentation()` builder extension — subscribe to the meter and the activity source by name, as above.

## Validator instrumentation

`AddOrionGuardOpenTelemetry()` replaces every closed, non-keyed `IValidator<T>` registration already in the collection with an `InstrumentedValidator<T>` that wraps the original and keeps its lifetime. `InstrumentedValidator<T>` implements every `IValidator<T>` overload and passes the `ValidationContext` through, so context-aware rules keep working.

Meter and `ActivitySource` name: `Moongazing.OrionGuard` (`OrionGuardInstrumentation.MeterName`, `OrionGuardInstrumentation.ActivitySourceName`); the instrumentation version is the package version.

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `orionguard.validations.total` | `Counter<long>` | none | none |
| `orionguard.validations.failures` | `Counter<long>` | none | none |
| `orionguard.validations.duration_ms` | `Histogram<double>` | ms | none |

Spans are named `OrionGuard.Validate` or `OrionGuard.ValidateAsync` after the method actually called, and carry `orionguard.validator_type` (the model type's name), `orionguard.validation_result` (`success` or `failed`) and, on failure, `orionguard.error_count`.

## Domain-event instrumentation

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.OpenTelemetry.DomainEvents;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

public static class DomainEventTelemetrySetup
{
    public static void Add(IServiceCollection services)
    {
        services.AddOrionGuardDomainEvents();
        services.WithOpenTelemetryDomainEvents();

        services.AddOpenTelemetry()
            .WithMetrics(m => m.AddMeter(OrionGuardDomainEventTelemetry.MeterName))
            .WithTracing(t => t.AddSource(OrionGuardDomainEventTelemetry.ActivitySourceName));
    }
}
```

`WithOpenTelemetryDomainEvents()` wraps the last registered `IDomainEventDispatcher` in an `InstrumentedDomainEventDispatcher` of the same lifetime. It throws `InvalidOperationException` when no dispatcher is registered, and a second call does nothing.

Meter and `ActivitySource` name: `Moongazing.OrionGuard.DomainEvents`.

| Instrument | Type | Unit | Tags |
| --- | --- | --- | --- |
| `orionguard.domain_events.dispatched` | `Counter<long>` | events | `event_type` |
| `orionguard.domain_events.failed` | `Counter<long>` | events | `event_type` |
| `orionguard.domain_events.duration` | `Histogram<double>` | ms | `event_type` |

`event_type` is the event's short CLR type name. Each event — including each event of a batch — gets its own `Internal` span named `DomainEvent.Dispatch <EventTypeName>`, tagged with `orionguard.event.id`, `orionguard.event.type` (full name) and `orionguard.event.occurred_on` (ISO 8601 round-trip). A failure sets the span status to `Error` with the exception message, adds an `exception` event carrying `exception.type` and `exception.message`, and rethrows.

## Ordering

Call `AddOrionGuardOpenTelemetry()` **after** every validator is registered — it decorates what is in the collection at that moment, and a validator registered later is not instrumented.

With the MediatR event bridge the order is `AddOrionGuardDomainEvents()`, then `AddOrionGuardMediatRDomainEvents()`, then `WithOpenTelemetryDomainEvents()`. The bridge refuses to run after the decorator, because replacing the registration would silently remove it.

## What this does not do

- **It only sees validation that goes through a DI-resolved `IValidator<T>`.** `Guard.Against...`, `Ensure.That(...)`, `Validate.For(...)` and `AttributeValidator` are invisible to it — they never touch the container.
- **Open-generic and keyed registrations are left alone.** `AddTransient(typeof(IValidator<>), typeof(MyValidator<>))` and keyed validators are not instrumented (and, deliberately, not broken either).
- **No metric carries the model type.** The three validator instruments have no tags at all; the type is on the span, not on the counters, so you cannot split failure rate by model in a metrics backend. The counts stay cheap because of it.
- **It exports nothing on its own.** Without an SDK and an exporter subscribed to the two names, the instruments and spans are inert.
- **It adds a layer per validator.** One extra object and one span per validation — small, but not free on a hot path that validates millions of times.

## Targets

`net8.0`, `net9.0`, `net10.0`; `OpenTelemetry.Api` 1.x.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) · [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR) (the MediatR event bridge) · [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) · [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (the outbox has its own meter)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
