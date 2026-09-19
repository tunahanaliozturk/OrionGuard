# Packages

The core package is `OrionGuard`. Everything else is optional and adds OrionGuard to one place:
a transport, a framework, or a build step. Each page below is that package's README, so it matches
what NuGet shows for the same version.

All runtime packages target `net8.0`, `net9.0` and `net10.0`. The two Roslyn components target
`netstandard2.0`, and the migration tool runs on .NET 10.

## Core

| Package | What it adds |
| --- | --- |
| [OrionGuard](orionguard.md) | Guard clauses, object validators, the JSON rule engine, DDD primitives and domain events |

## Web and transports

| Package | What it adds |
| --- | --- |
| [OrionGuard.AspNetCore](aspnetcore.md) | RFC 9457 ProblemDetails, a Minimal API endpoint filter, `[ValidateRequest]` for MVC, options validation |
| [OrionGuard.MediatR](mediatr.md) | A pipeline behavior that validates requests, and a MediatR-backed domain-event dispatcher |
| [OrionGuard.MassTransit](masstransit.md) | A consume filter that validates messages before the consumer runs |
| [OrionGuard.Blazor](blazor.md) | `EditForm` validation components |
| [OrionGuard.Grpc](grpc.md) | A server interceptor that validates incoming requests |
| [OrionGuard.SignalR](signalr.md) | A hub filter that validates hub method arguments |
| [OrionGuard.Hangfire](hangfire.md) | Validation of background job arguments at enqueue time |

## Contracts and build-time

| Package | What it adds |
| --- | --- |
| [OrionGuard.Generators](generators.md) | `[GenerateValidator]` compile-time validators and the `OG0001` analyzer |
| [OrionGuard.OpenApi](openapi.md) | A validator generated from an OpenAPI 3 schema |
| [OrionGuard.Swagger](swagger.md) | OrionGuard attribute constraints written into Swashbuckle schemas |
| [OrionGuard.Migration](migration.md) | A `dotnet tool` that rewrites FluentValidation validators |

## Domain events, outbox and operations

| Package | What it adds |
| --- | --- |
| [OrionGuard.EntityFrameworkCore](entityframeworkcore.md) | A `SaveChanges` interceptor that dispatches domain events inline or through a transactional outbox |
| [OrionGuard.Outbox.PostgresNotify](outbox-postgresnotify.md) | PostgreSQL `LISTEN`/`NOTIFY` wake-up for the outbox dispatcher |
| [OrionGuard.Outbox.SqlServerBroker](outbox-sqlserverbroker.md) | SQL Server Service Broker wake-up for the outbox dispatcher |
| [OrionGuard.Outbox.Dashboard](outbox-dashboard.md) | Endpoints to list, replay and discard failed outbox rows |
| [OrionGuard.Locks.Redis](locks-redis.md) | A Redis `IDistributedLock` for outbox replicas |
| [OrionGuard.OpenTelemetry](opentelemetry.md) | Metrics and traces for validation and domain-event dispatch |
| [OrionGuard.Testing](testing.md) | Domain-event capture, an in-memory dispatcher, and assertions for tests |

Packages announced for 7.0.0 are listed under [In development for 7.0.0](../docs/coming-in-7.md).
