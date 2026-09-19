# API reference

Generated from the XML documentation in the source, for the `net10.0` build of each package. The
`net8.0` and `net9.0` builds expose the same public API; where behaviour differs per target
framework, the remarks say so.

Not included: `OrionGuard.Generators` and `OrionGuard.OpenApi`, which are Roslyn components that emit
code into your project rather than offering a runtime API, and `OrionGuard.Migration`, which is a
command-line tool. Their pages are under [Packages](../packages/index.md). Packages still in
development are not here either; they are listed on
[In development for 7.0.0](../docs/coming-in-7.md).

## Core

- [Moongazing.OrionGuard.Core](xref:Moongazing.OrionGuard.Core) — `Ensure`, `Guard`, `FastGuard`, `GuardResult`, `Validate`, `AbstractValidator`, `FluentGuard`
- [Moongazing.OrionGuard.Extensions](xref:Moongazing.OrionGuard.Extensions) — the extension guards, including `SecurityGuards`
- [Moongazing.OrionGuard.DynamicRules](xref:Moongazing.OrionGuard.DynamicRules) — `DynamicValidator` and the JSON rule model
- [Moongazing.OrionGuard.Compatibility](xref:Moongazing.OrionGuard.Compatibility) — `FluentStyleValidator<T>`
- [Moongazing.OrionGuard.DependencyInjection](xref:Moongazing.OrionGuard.DependencyInjection) — `IValidator<T>` and the registration extensions
- [Moongazing.OrionGuard.Attributes](xref:Moongazing.OrionGuard.Attributes), [Exceptions](xref:Moongazing.OrionGuard.Exceptions), [Localization](xref:Moongazing.OrionGuard.Localization), [Profiles](xref:Moongazing.OrionGuard.Profiles), [Utilities](xref:Moongazing.OrionGuard.Utilities)

## Domain model

- [Moongazing.OrionGuard.Domain.Primitives](xref:Moongazing.OrionGuard.Domain.Primitives) — `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, strongly-typed ids
- [Moongazing.OrionGuard.Domain.Events](xref:Moongazing.OrionGuard.Domain.Events) — events, handlers, dispatchers
- [Moongazing.OrionGuard.Domain.Rules](xref:Moongazing.OrionGuard.Domain.Rules), [Domain.Exceptions](xref:Moongazing.OrionGuard.Domain.Exceptions)

## Integrations

- [Moongazing.OrionGuard.AspNetCore](xref:Moongazing.OrionGuard.AspNetCore)
- [Moongazing.OrionGuard.MediatR](xref:Moongazing.OrionGuard.MediatR)
- [Moongazing.OrionGuard.MassTransit](xref:Moongazing.OrionGuard.MassTransit)
- [Moongazing.OrionGuard.Blazor](xref:Moongazing.OrionGuard.Blazor)
- [Moongazing.OrionGuard.Grpc](xref:Moongazing.OrionGuard.Grpc)
- [Moongazing.OrionGuard.SignalR](xref:Moongazing.OrionGuard.SignalR)
- [Moongazing.OrionGuard.Hangfire](xref:Moongazing.OrionGuard.Hangfire)
- [Moongazing.OrionGuard.Swagger](xref:Moongazing.OrionGuard.Swagger)
- [Moongazing.OrionGuard.OpenTelemetry](xref:Moongazing.OrionGuard.OpenTelemetry)
- [Moongazing.OrionGuard.Testing.DomainEvents](xref:Moongazing.OrionGuard.Testing.DomainEvents)

## Domain events in EF Core, and the outbox

- [Moongazing.OrionGuard.EntityFrameworkCore](xref:Moongazing.OrionGuard.EntityFrameworkCore)
- [EntityFrameworkCore.Outbox](xref:Moongazing.OrionGuard.EntityFrameworkCore.Outbox) and its [Archival](xref:Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival), [Locking](xref:Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking), [Push](xref:Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push) and [TypeMap](xref:Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap) namespaces
- [Moongazing.OrionGuard.Outbox.Dashboard](xref:Moongazing.OrionGuard.Outbox.Dashboard)
- [Moongazing.OrionGuard.Outbox.PostgresNotify](xref:Moongazing.OrionGuard.Outbox.PostgresNotify), [Outbox.SqlServerBroker](xref:Moongazing.OrionGuard.Outbox.SqlServerBroker)
- [Moongazing.OrionGuard.Locks.Redis](xref:Moongazing.OrionGuard.Locks.Redis)

Use the table of contents on the left, or the search box, to reach an individual type.
