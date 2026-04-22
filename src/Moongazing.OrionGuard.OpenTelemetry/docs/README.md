# OrionGuard.OpenTelemetry

OpenTelemetry instrumentation for [**OrionGuard**](https://github.com/tunahanaliozturk/OrionGuard). Emits validation metrics and distributed tracing spans so you can see how your validators behave in production.

## What this package emits

**Metrics** (Meter: `Moongazing.OrionGuard`):

- `orionguard.validation.count` — total validations run, tagged by `validator` and `result` (valid/invalid).
- `orionguard.validation.failures` — validations that produced errors, tagged by `validator` and `error_code`.
- `orionguard.validation.duration` — histogram of validation latency in milliseconds, tagged by `validator`.

**Traces** (ActivitySource: `Moongazing.OrionGuard`):

- `OrionGuard.Validate` spans around every validator execution with tags for input type, result, and error count.
- Automatic correlation with inbound ASP.NET Core / gRPC / SignalR activities.

## Install

```bash
dotnet add package OrionGuard.OpenTelemetry
```

Requires OpenTelemetry in your application. The core `OrionGuard` package is brought in transitively.

## Quick start

```csharp
using Moongazing.OrionGuard.OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddOrionGuardInstrumentation().AddPrometheusExporter())
    .WithTracing(t => t.AddOrionGuardInstrumentation().AddOtlpExporter());
```

After that, any validator executed through `Ensure`, `Validate.For`, `Validate.Nested`, or a registered `IValidator<T>` is automatically measured and traced.

## Targets

.NET 8.0, .NET 9.0, .NET 10.0.

## License

MIT. See the [main repository](https://github.com/tunahanaliozturk/OrionGuard) for full docs, CHANGELOG, and samples.
