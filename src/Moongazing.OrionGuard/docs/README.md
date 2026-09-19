# OrionGuard

Guard clauses, object validation, and DDD building blocks for .NET in one package: guards throw on bad input, validators collect every error into a result, and entity, aggregate, and business-rule primitives raise domain events. Source, full documentation, and samples: [github.com/tunahanaliozturk/OrionGuard](https://github.com/tunahanaliozturk/OrionGuard).

## Install

```bash
dotnet add package OrionGuard
```

## Quick start

```csharp
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Extensions;

public static class Registration
{
    public static void Register(string email, string password, int age, string displayName)
    {
        // Throws on the first failure. The parameter name ("email", "age", ...) is captured
        // automatically through CallerArgumentExpression.
        Ensure.That(email).NotNull().NotEmpty().Email();
        Ensure.That(password).NotNull().MinLength(8);
        Ensure.That(age).InRange(18, 120);

        // Span-based guard for hot paths.
        FastGuard.NotNullOrEmpty(displayName, nameof(displayName));

        // Heuristic: rejects input that matches known SQL injection patterns. It is not a defence;
        // the query that uses displayName must still be parameterized.
        displayName.AgainstSqlInjection(nameof(displayName));
    }
}
```

## Features

### Guards and validation

- `Ensure.That(value)` fluent guards: null and empty checks, length, regex, email, URL, numeric ranges, collection counts, dates, `When` / `Unless` conditions, and `Transform` / `Default` value pipelines.
- `Ensure.Accumulate(value)` with `GuardResult`: collect every error instead of throwing, merge results with `GuardResult.Combine`, and read them with `ToErrorDictionary()`. Errors carry a `Severity` (Error, Warning, Info) and results can carry a `SuggestedHttpStatusCode`.
- `FastGuard`: span-based guards (`NotNullOrEmpty`, `Email`, `Ascii`, `AlphaNumeric`, `MaxLength`, `ValidGuid`, `Finite`, ...) with the throw paths moved into separate `[DoesNotReturn]` helpers to keep the hot path small.
- Extension guards in `Moongazing.OrionGuard.Extensions`:
  - Injection heuristics: `AgainstSqlInjection`, `AgainstXss`, `AgainstCommandInjection`, `AgainstLdapInjection`, `AgainstXxe`, and `AgainstInjection` (the checks combined, for free text). These are denylists, not a defence: they miss payloads they do not list and reject some ordinary text. Keep using parameterized queries, contextual output encoding, `ProcessStartInfo.ArgumentList` without a shell, LDAP escaping, and an XML reader with DTDs prohibited.
  - Paths and redirects: `AgainstPathTraversal`, `AgainstPathEscape` (resolves a path and keeps it inside a root directory), `AgainstUnsafeFileName`, and `AgainstOpenRedirect` (local paths or allow-listed hosts, ASP.NET Core `IsLocalUrl` rules).
  - Format: latitude/longitude, MAC address, hostname, CIDR, ISO 3166 country code, IANA time zone, BCP 47 language tag, JWT structure, connection string, Base64.
  - International: SWIFT/BIC, ISBN, VIN, EAN, EU VAT number, IMEI.
  - Business: monetary amount, currency code, SKU, coupon code, discount, status transitions, business hours, date ranges.
  - Rate limits: `AgainstRateLimitExceeded`, `AgainstSlidingWindowExceeded`, `AgainstDailyQuotaExceeded`, and more.
- Validation messages in 14 languages: English, Turkish, German, French, Spanish, Portuguese, Arabic, Japanese, Chinese, Korean, Russian, Dutch, Polish, Italian. Set the culture per request with `ValidationMessages.SetCultureForCurrentScope`.
- Exceptions: `Guard` and `Ensure` throw `GuardException` or one of its subclasses (`NullValueException`, `OutOfRangeException`, ...), and most extension guards throw `ArgumentException`. To use your own exception types, catch these at your boundary and rethrow. `IExceptionFactory` is never called by the guards; `AddOrionGuardExceptionFactory<TFactory>()`, `ExceptionFactoryProvider.Configure` and `DefaultExceptionFactory` are obsolete and will be removed in v7.
- Regex patterns use `[GeneratedRegex]` source generation, so nothing is compiled at runtime.

### Object validation

- `Validate.For(obj)`: property rules with cached, compiled property accessors. Add I/O-bound rules with `MustAsync` and finish with `ToResultAsync` or `ThrowIfInvalidAsync`.
- `Validate.Nested(obj)` for deep object graphs and collections, `Validate.CrossProperties(obj)` for rules across properties, and `Validate.Polymorphic<T>()` for per-subtype rules.
- `AbstractValidator<T>` with named rule sets (`RuleSet("create", ...)`, then `Validate(value, RuleSet.Create)`) and `ValidateAsync`. Wrap any `IValidator<T>` in a `CachedValidator<T>` with `validator.WithCaching()`: results are cached when `T` is a record with compiler-synthesized equality, or by an explicit key with `validator.WithCaching(order => (order.Id, order.Version))`. Other types, and calls with a non-empty `ValidationContext`, always run the inner validator.
- `DynamicValidator.FromJson(json)`: rules loaded at runtime from JSON (a database, config file, or API).
- Attribute validation: `[NotNull]`, `[NotEmpty]`, `[Length]`, `[Email]`, `[Range]`, `[Regex]`, `[Positive]` checked with `AttributeValidator.Validate(obj)`.
- FluentValidation-style syntax: `FluentStyleValidator<T>` in `Moongazing.OrionGuard.Compatibility` supports `RuleFor(x => x.Email).NotEmpty().EmailAddress()`. The `OrionGuard.Migration` tool rewrites existing FluentValidation validators onto it.
- Dependency injection: `services.AddOrionGuard()` and `services.AddValidator<T, TValidator>()`. `ValidatorInvoker.ValidateAsync(services, instance)` runs every `IValidator<T>` registered for an object's runtime type and combines the results (`null` when none is registered); the transport integrations use it.

### DDD and domain events

- `Entity<TId>` (identity equality), `AggregateRoot<TId>` (`RaiseEvent`, `PullDomainEvents()`), and the `IAggregateRoot` marker.
- `ValueObject` base class, plus the `IValueObject` marker for record-based value objects.
- `StronglyTypedId<TValue>` abstract record and the `IStronglyTypedId<TValue>` interface, with the `AgainstDefaultStronglyTypedId` guard.
- Business rules: derive from `BusinessRule` or `AsyncBusinessRule`, then enforce them with `Guard.AgainstBrokenRule` / `Guard.AgainstBrokenRuleAsync`, or `CheckRule` / `CheckRuleAsync` inside an entity. A broken rule throws `BusinessRuleValidationException`.
- Domain events: `IDomainEvent`, the `DomainEventBase` record, `IDomainEventHandler<TEvent>`, and `IDomainEventDispatcher`. Register the default dispatcher with `services.AddOrionGuardDomainEvents()` and your handlers with `services.AddOrionGuardDomainEventHandlers(assembly)`. Dispatch modes: `SequentialFailFast` (default), `SequentialContinueOnError`, `Parallel`.

## Collect all errors instead of throwing

```csharp
using Moongazing.OrionGuard.Core;

public static class SignUpValidation
{
    public static Dictionary<string, string[]>? Check(string email, string password)
    {
        GuardResult result = GuardResult.Combine(
            Ensure.Accumulate(email).NotNull().Email().ToResult(),
            Ensure.Accumulate(password).NotNull().MinLength(8).ToResult());

        // e.g. { "email": [...], "password": [...] }, ready for a ValidationProblem response
        return result.IsInvalid ? result.ToErrorDictionary() : null;
    }
}
```

## Validate an object, including async rules

```csharp
using Moongazing.OrionGuard.Core;

public sealed record CreateUser(string Email, string Password, int Age);

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);
}

public sealed class CreateUserValidation(IUserRepository users)
{
    public Task<GuardResult> ValidateAsync(CreateUser input, CancellationToken cancellationToken) =>
        Validate.For(input)
            .Property(u => u.Email, g => g.NotNull().Email())
            .Property(u => u.Password, g => g.NotNull().MinLength(8))
            .Property(u => u.Age, g => g.InRange(18, 120))
            .MustAsync(
                u => u.Email,
                async (email, ct) => !await users.EmailExistsAsync(email, ct),
                "Email is already registered.",
                "EMAIL_TAKEN")
            .ToResultAsync(cancellationToken);
}
```

Once a validator has a `MustAsync` rule, use an async terminal (`ToResultAsync`, `BuildAsync`, `ThrowIfInvalidAsync`). The synchronous `ToResult()` throws `InvalidOperationException` instead of skipping the async rules.

## An aggregate with a business rule and a domain event

```csharp
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Domain.Rules;

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);

public sealed record OrderShipped(OrderId OrderId) : DomainEventBase;

public sealed class OrderMustBePaid(bool isPaid) : BusinessRule
{
    public override bool IsBroken() => !isPaid;
    public override string DefaultMessage => "An order must be paid before it ships.";
}

public sealed class Order : AggregateRoot<OrderId>
{
    public Order(OrderId id) : base(id) { }

    public bool IsPaid { get; private set; }

    public void MarkPaid() => IsPaid = true;

    public void Ship()
    {
        CheckRule(new OrderMustBePaid(IsPaid)); // throws BusinessRuleValidationException when broken
        RaiseEvent(new OrderShipped(Id));
    }
}
```

Handle the event, register the dispatcher, and dispatch after the aggregate is saved:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;

public sealed class SendShippingEmail : IDomainEventHandler<OrderShipped>
{
    public Task HandleAsync(OrderShipped @event, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ShipOrder(IDomainEventDispatcher dispatcher)
{
    public async Task HandleAsync(Order order, CancellationToken cancellationToken)
    {
        order.Ship();
        // ...persist the order, then publish what it raised:
        await dispatcher.DispatchAsync(order.PullDomainEvents(), cancellationToken);
    }
}

public static class DomainEventSetup
{
    public static IServiceCollection AddShipping(this IServiceCollection services) =>
        services
            .AddOrionGuardDomainEvents()
            .AddOrionGuardDomainEventHandlers(typeof(SendShippingEmail).Assembly)
            .AddScoped<ShipOrder>();
}
```

`OrionGuard.EntityFrameworkCore` can do the pull-and-dispatch step for you on `SaveChanges`, either inline or through a transactional outbox.

## Integrations

| Package | Purpose |
| --- | --- |
| `OrionGuard.AspNetCore` | Middleware, Minimal API endpoint filters, MVC action filters, RFC 9457 ProblemDetails |
| `OrionGuard.MediatR` | `ValidationBehavior` pipeline behavior for CQRS requests, plus a MediatR-backed domain-event dispatcher |
| `OrionGuard.Blazor` | `EditForm` validation components |
| `OrionGuard.Grpc` | Server interceptor that validates incoming requests |
| `OrionGuard.SignalR` | Hub filter that validates hub method arguments |
| `OrionGuard.Hangfire` | Validates background job arguments at enqueue time |
| `OrionGuard.MassTransit` | Consume filter that validates each message before it reaches the consumer (MassTransit 8.x) |
| `OrionGuard.Swagger` | Writes OrionGuard attribute constraints into Swashbuckle OpenAPI schemas |
| `OrionGuard.OpenApi` | Source generator that builds an OrionGuard validator from an OpenAPI 3 schema |
| `OrionGuard.OpenTelemetry` | Metrics and tracing for validation and domain-event dispatch |
| `OrionGuard.EntityFrameworkCore` | `SaveChanges` interceptor that dispatches domain events inline or through a transactional outbox |
| `OrionGuard.Outbox.PostgresNotify` | PostgreSQL `LISTEN`/`NOTIFY` wake signal for the outbox dispatcher |
| `OrionGuard.Outbox.SqlServerBroker` | SQL Server Service Broker wake signal for the outbox dispatcher |
| `OrionGuard.Outbox.Dashboard` | ASP.NET Core endpoints to list, replay, and discard failed outbox messages |
| `OrionGuard.Locks.Redis` | Redis-backed `IDistributedLock` for multi-instance outbox dispatchers, through OrionLock.Redis |
| `OrionGuard.Generators` | `[GenerateValidator]` compile-time validators and the `OG0001` analyzer |
| `OrionGuard.Testing` | Domain-event capture, an in-memory dispatcher, and assertions for tests |
| `OrionGuard.Migration` | dotnet tool that migrates FluentValidation validators to OrionGuard |

## Targets

- `net8.0`, `net9.0`, `net10.0`
- One dependency: `Microsoft.Extensions.DependencyInjection.Abstractions`

## Documentation

- [Full README and feature guide](https://github.com/tunahanaliozturk/OrionGuard#readme)
- [CHANGELOG](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- [Migrating from the `[StronglyTypedId]` generator to OrionKey](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/stronglytypedid-to-orionkey.md) (applies to the generator only; the `StronglyTypedId<TValue>` record above is not deprecated)
- Related packages: [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore), [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore), [OrionGuard.Testing](https://www.nuget.org/packages/OrionGuard.Testing), [OrionGuard.Generators](https://www.nuget.org/packages/OrionGuard.Generators), [OrionGuard.Migration](https://www.nuget.org/packages/OrionGuard.Migration)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
