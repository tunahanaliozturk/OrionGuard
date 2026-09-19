# OrionGuard

Guard clauses, object validation, and DDD building blocks in one package, for .NET services that want bad input rejected at the boundary and every problem reported at once.

```bash
dotnet add package OrionGuard
```

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

        // Heuristic: rejects input that matches known SQL injection patterns. It is not a
        // defence; the query that uses displayName must still be parameterized.
        displayName.AgainstSqlInjection(nameof(displayName));
    }
}
```

You get back a `GuardException` (or one of its subclasses: `NullValueException`, `OutOfRangeException`, `InvalidEmailException`, ...) carrying the parameter name and a message in the calling thread's culture. Nothing is registered, nothing is configured, and no reflection runs on this path.

When you would rather collect the failures than throw, the same rules return a `GuardResult` that lists every error, carries a severity and an optional HTTP status, and converts to the dictionary shape `ValidationProblem` expects.

## Throw on the first bad value

`Ensure.That(value)` is the fluent entry point; `Guard` holds the same checks as plain static methods, and `FastGuard` is a span-based subset for hot paths.

```csharp
using Moongazing.OrionGuard.Core;

public static class ThrowingGuards
{
    public static void Check(string sku, decimal price, DateTime shipOn, IReadOnlyList<string> tags)
    {
        Ensure.That(sku).NotNull().Matches("^[A-Z0-9-]+$");
        Ensure.That(price).Positive();
        Ensure.That(shipOn).InFuture();
        Ensure.That(tags).NotEmpty().MaxCount(10);

        // When / Unless gate every rule after them in the chain; Always() re-enables them.
        Ensure.That(sku).When(s => s.Length > 3).MaxLength(32).Always().NotNull();
    }
}
```

Numeric comparisons (`GreaterThan`, `LessThan`, `InRange`, `Positive`, `NotNegative`, `NotZero`) compare by value across the built-in numeric types, so `GreaterThan(0)` on a `decimal` or `long` behaves the way it reads. A value that cannot be compared with the threshold, and `NaN`, fail the rule rather than slipping through.

Date guards normalize to UTC before comparing. A `DateTimeKind.Unspecified` value is treated as UTC; a local value is converted first, so a machine east of UTC does not reject its own `DateTime.Now`.

## Collect every error instead

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

`GuardResult` exposes `Errors`, `Warnings` and `Infos` separately (each `ValidationError` carries a `Severity`), `IsValid` / `IsInvalid`, `GetErrorSummary()`, `ToErrorDictionary()` and `ThrowIfInvalid()`. `GuardResult.FailureWithStatus(409, ...)` suggests an HTTP status, which `Combine` and `Merge` preserve and which the ASP.NET Core filters use for the response code.

## Validate an object

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

Once a validator has a `MustAsync` rule you must finish with an async terminal (`ToResultAsync`, `BuildAsync`, `ThrowIfInvalidAsync`); the synchronous `ToResult()` throws `InvalidOperationException` rather than quietly skipping the async rules.

Other entry points for the same job:

| Entry point | Use it for |
| --- | --- |
| `Validate.For(obj)` / `Validate.ForStrict(obj)` | Property rules on one object |
| `Validate.Nested(obj)` | Deep object graphs and collections |
| `Validate.CrossProperties(obj)` | Rules that span two properties |
| `Validate.Delta(original, updated)` | Rules about what changed |
| `Validate.Polymorphic<T>()` | A different rule set per subtype |
| `AbstractValidator<T>` | A reusable, injectable validator class |
| `AttributeValidator.Validate(obj)` | `[NotNull]`, `[NotEmpty]`, `[Length]`, `[Email]`, `[Range]`, `[Regex]`, `[Positive]` on the model |
| `DynamicValidator.FromJson(json)` | Rules that arrive at runtime from a database or config |
| `FluentStyleValidator<T>` | FluentValidation's `RuleFor(...)` syntax during a migration |

`AbstractValidator<T>` supports named rule sets — declare them with `RuleSet("create", ...)` and select one with `Validate(value, RuleSet.Create)`.

Any `IValidator<T>` can be wrapped in a result cache with `validator.WithCaching()`. Without a key selector, results are cached only for records with compiler-synthesized equality, because those are the only models whose equality is known to cover every field; anything else, including a type with a hand-written `IEquatable<T>`, runs the inner validator every time. Pass a key when you know what identity means: `validator.WithCaching(order => (order.Id, order.Version))`. A call carrying a non-empty `ValidationContext` is never served from the cache.

Register validators with `services.AddOrionGuard()` plus `services.AddValidator<CreateUser, CreateUserValidator>()`. `ValidatorInvoker.ValidateAsync(serviceProvider, instance)` runs *every* `IValidator<T>` registered for an object's runtime type and combines the results, returning `null` when none is registered; that is the shared path the ASP.NET Core, gRPC, SignalR, Hangfire and MassTransit integrations use.

## Domain model, rules and events

```csharp
namespace Shipping;

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

`RaiseEvent` only records the event. Something has to pull and publish it:

```csharp
namespace Shipping;

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

`AddOrionGuardDomainEvents()` takes a dispatch mode: `SequentialFailFast` (the default — the first handler that throws stops the rest), `SequentialContinueOnError`, or `Parallel`. `OrionGuard.EntityFrameworkCore` does the pull-and-dispatch step for you on `SaveChanges`, inline or through a transactional outbox.

The rest of the domain surface: `Entity<TId>` (identity equality), `ValueObject` and the `IValueObject` marker, `StronglyTypedId<TValue>` with `IStronglyTypedId<TValue>` and the `AgainstDefaultStronglyTypedId` guard, and `BusinessRule` / `AsyncBusinessRule` enforced with `CheckRule` / `CheckRuleAsync` inside an entity or `Guard.AgainstBrokenRule` / `AgainstBrokenRuleAsync` anywhere else.

## Security and format guards

In `Moongazing.OrionGuard.Extensions`, as extension methods on the value:

- **Injection heuristics** — `AgainstSqlInjection`, `AgainstXss`, `AgainstCommandInjection`, `AgainstLdapInjection`, `AgainstXxe`, and `AgainstInjection` (all of them, for free text). Read the limits below before you rely on these.
- **Paths and redirects** — `AgainstPathTraversal` (checked as given, after up to three URL decodes, and after NFKC normalization), `AgainstPathEscape(root)` (resolves the path and returns it only if it stays inside `root`), `AgainstUnsafeFileName`, `AgainstOpenRedirect` (ASP.NET Core `IsLocalUrl` rules plus an allow-list of absolute hosts).
- **Files** — `AgainstDangerousFileExtension`, `AgainstDisallowedExtension`, `AgainstMaliciousContent` (scans the whole array, or a stream with an explicit `maxScanBytes`), `AgainstFakeMimeType`.
- **Secrets and PII** — `AgainstContainsCreditCardNumber`, `AgainstContainsSecret`, `AgainstContainsPii`.
- **Formats** — latitude/longitude, MAC address, hostname, CIDR, ISO 3166 country code, IANA time zone, BCP 47 language tag, JWT structure, connection string, Base64, credit card (Luhn plus a 12–19 ASCII digit check).
- **International** — SWIFT/BIC, ISBN, VIN, EAN, EU VAT number, IMEI.
- **Business** — monetary amount, currency code, SKU, coupon code, discount, status transitions, business hours, date ranges.
- **Quotas** — `AgainstRateLimitExceeded`, `AgainstSlidingWindowExceeded`, `AgainstDailyQuotaExceeded`: threshold checks over a count you supply.

`Moongazing.OrionGuard.Utilities.LdapEncoding` has the actual LDAP defence the guards are not: `EscapeFilterValue` (RFC 4515) and `EscapeDistinguishedNameValue` (RFC 4514).

Every pattern is a `[GeneratedRegex]`, so nothing is compiled at runtime, anchored patterns end at `\z` rather than `$` (a trailing newline does not slip through), and `[0-9]` is used instead of `\d` so non-ASCII digits are not accepted as numbers. Matching is bounded at one second; in the result-returning APIs a timeout is reported as a validation error, and in the throwing APIs it becomes that guard's own exception rather than `RegexMatchTimeoutException`.

## Messages and culture

Messages ship in 14 languages: English, Turkish, German, French, Spanish, Portuguese, Arabic, Japanese, Chinese, Korean, Russian, Dutch, Polish, Italian. With no culture set, messages follow the calling thread's `CurrentCulture`. Set one per request with `ValidationMessages.SetCultureForCurrentScope(culture)` (`AsyncLocal`, so it does not leak between requests), process-wide with `SetCulture`, or replace the lookup entirely with `SetMessageResolver`. `AddMessages(cultureName, ...)` adds or overrides keys.

## With the rest of OrionGuard

| Package | What it adds |
| --- | --- |
| [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) | Middleware, Minimal API endpoint filters, MVC action filters, RFC 9457 ProblemDetails |
| [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR) | Validation pipeline behaviors for requests and streams, and a MediatR-backed event dispatcher |
| [OrionGuard.MassTransit](https://www.nuget.org/packages/OrionGuard.MassTransit) | Consume filter that validates a message before the consumer sees it |
| [OrionGuard.Blazor](https://www.nuget.org/packages/OrionGuard.Blazor) | `EditForm` validation components |
| [OrionGuard.Grpc](https://www.nuget.org/packages/OrionGuard.Grpc) | Server interceptor for unary and streaming calls |
| [OrionGuard.SignalR](https://www.nuget.org/packages/OrionGuard.SignalR) | Hub filter that validates hub method arguments |
| [OrionGuard.Hangfire](https://www.nuget.org/packages/OrionGuard.Hangfire) | Rejects an invalid background job at enqueue time |
| [OrionGuard.Swagger](https://www.nuget.org/packages/OrionGuard.Swagger) | Writes OrionGuard attribute constraints into Swashbuckle schemas |
| [OrionGuard.OpenApi](https://www.nuget.org/packages/OrionGuard.OpenApi) | Generates a validator from an OpenAPI 3 schema at build time |
| [OrionGuard.SchemaExport](https://www.nuget.org/packages/OrionGuard.SchemaExport) | Exports a model's attribute rules as JSON Schema or a TypeScript interface |
| [OrionGuard.Generators](https://www.nuget.org/packages/OrionGuard.Generators) | `[GenerateValidator]` compile-time validators, no reflection |
| [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry) | Metrics and spans for validation and event dispatch |
| [OrionGuard.Aspire](https://www.nuget.org/packages/OrionGuard.Aspire) | `builder.AddOrionGuardDefaults()`: every OrionGuard meter, activity source and health check in an Aspire app |
| [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) | Dispatches domain events on `SaveChanges`, inline or through a transactional outbox |
| [OrionGuard.Outbox.PostgresNotify](https://www.nuget.org/packages/OrionGuard.Outbox.PostgresNotify) | Wakes the outbox dispatcher on PostgreSQL `LISTEN`/`NOTIFY` |
| [OrionGuard.Outbox.SqlServerBroker](https://www.nuget.org/packages/OrionGuard.Outbox.SqlServerBroker) | Wakes the outbox dispatcher on SQL Server Service Broker |
| [OrionGuard.Outbox.Dashboard](https://www.nuget.org/packages/OrionGuard.Outbox.Dashboard) | Endpoints to list, replay and discard failed outbox messages |
| [OrionGuard.Locks.Redis](https://www.nuget.org/packages/OrionGuard.Locks.Redis) | Redis lease so several outbox dispatchers do not overlap |
| [OrionGuard.Testing](https://www.nuget.org/packages/OrionGuard.Testing) | Domain-event capture, an in-memory dispatcher, and assertions |
| [OrionGuard.Migration](https://www.nuget.org/packages/OrionGuard.Migration) | dotnet tool that rewrites FluentValidation validators onto OrionGuard |
| [OrionGuard.Templates](https://www.nuget.org/packages/OrionGuard.Templates) | `dotnet new` templates that start a project with this wiring already done |

## What this does not do

- **The injection guards are not a defence.** `AgainstSqlInjection`, `AgainstXss`, `AgainstCommandInjection`, `AgainstLdapInjection`, `AgainstXxe` and `AgainstInjection` are denylists. They miss payloads they do not list, and they reject some ordinary text. Keep the real controls: parameterized queries, contextual output encoding, `ProcessStartInfo.ArgumentList` without a shell, RFC 4515/4514 escaping (`LdapEncoding`), and an `XmlReader` with DTD processing prohibited. Use the guards as a tripwire in front of those, never instead of them.
- **`AgainstFakeMimeType` only knows file types that have a magic-number signature.** `.txt`, `.csv`, `.html` and anything else without one pass whatever their content is. The control for those is an allow-list of extensions (`AgainstDisallowedExtension`).
- **`AgainstInvalidHostname` is ASCII-only** — pass internationalized names in punycode. That is deliberate: it rejects look-alike hosts such as a Cyrillic `exаmple.com`.
- **The NFKC step does not survive every runtime, and it fails two different ways.** `AgainstPathTraversal` normalizes with `NormalizationForm.FormKC` to catch look-alike forms such as the fullwidth `．．／`; `AgainstUnsafeFileName` and `AgainstInjection` share that step.
  - **Globalization-invariant mode** (`InvariantGlobalization=true`) skips normalization entirely: [the string comes back unchanged and `IsNormalized` always returns `true`](https://github.com/dotnet/runtime/blob/main/docs/design/features/globalization-invariant-mode.md#string-normalization). Nothing throws, so nothing tells you the check weakened — the literal and URL-decoded passes still run while the normalized one quietly stops catching anything.
  - **Browser and WASI** (the default, non-invariant Blazor WebAssembly build) do the opposite: ASCII input is returned by a fast path and works, and any non-ASCII input **throws `PlatformNotSupportedException`**, because [browser ICU does not carry the data for `FormKC` and `FormKD`](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Private.CoreLib/src/System/Globalization/Normalization.Icu.cs). Setting `BlazorWebAssemblyLoadAllGlobalizationData` does not help: the check is on the platform and the normalization form, not on which ICU shard was loaded. The guards do not swallow it either — they catch only the `ArgumentException` that ill-formed UTF-16 raises — so it reaches your code.

  Run these guards on the server, which is the side that has to be convinced anyway. The [playground](https://github.com/tunahanaliozturk/OrionGuard/tree/master/site/playground) demonstrates the browser arm: the fullwidth `．．／secret.txt` sample reports the guard as unavailable rather than as a pass.
- **Attribute and dynamic validation use reflection.** `AttributeValidator` and `DynamicValidator` read properties at runtime, so they are not NativeAOT- or trimming-safe. `OrionGuard.Generators` exists for exactly that case.
- **`DynamicValidator` has a fixed rule vocabulary** — `NotNull`/`Required`, `NotEmpty`, `Length`, `MinLength`, `MaxLength`, `Range`, `GreaterThan`, `LessThan`, `Regex`/`Pattern`, `Email`, `Url`, `In`, `NotIn` (matched case-insensitively), each with a `WhenProperty`/`WhenValue` condition. There is no way to express a custom predicate in JSON; anything else needs code.
- **`FluentStyleValidator<T>` is a migration surface, not a FluentValidation clone.** It matches FluentValidation on the rules it implements, and `OrionGuard.Migration` reports rather than rewrites the ones where the semantics differ. It is not a drop-in for the whole FluentValidation API.
- **Nothing here dispatches domain events by itself.** `RaiseEvent` records; you (or `OrionGuard.EntityFrameworkCore`) must pull and publish.
- **`IExceptionFactory` never did anything** and is obsolete along with `ExceptionFactoryProvider`, `DefaultExceptionFactory` and `AddOrionGuardExceptionFactory<TFactory>()`; no guard has ever called it. They are removed in the next major version. Catch `GuardException` — or the specific subclass — at your boundary instead.
- **The rate-limit guards do not rate-limit.** `AgainstRateLimitExceeded(currentCount, maxAllowed, ...)`, `AgainstSlidingWindowExceeded` and `AgainstDailyQuotaExceeded` compare a count *you already have* against a limit and throw. Counting requests per key over a window is your store's job, or ASP.NET Core's rate-limiting middleware.

## Targets

`net8.0`, `net9.0`, `net10.0`. One dependency: `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Documentation

- [Full README and feature guide](https://github.com/tunahanaliozturk/OrionGuard#readme)
- [CHANGELOG](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- [Migrating from the `[StronglyTypedId]` generator to OrionKey](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/stronglytypedid-to-orionkey.md) — the generator only; the `StronglyTypedId<TValue>` record above is not deprecated

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
