<p align="center">
  <img src="src/Moongazing.OrionGuard/docs/logo.png" alt="OrionGuard Logo" width="150" />
</p>

<h1 align="center">OrionGuard</h1>

<p align="center">
  The most comprehensive guard clause &amp; validation ecosystem for .NET
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/OrionGuard"><img src="https://img.shields.io/nuget/v/OrionGuard?style=flat-square&color=blue" alt="NuGet" /></a>
  <a href="https://www.nuget.org/packages/OrionGuard"><img src="https://img.shields.io/nuget/dt/OrionGuard?style=flat-square&color=green" alt="Downloads" /></a>
  <a href="https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt"><img src="https://img.shields.io/badge/license-MIT-yellow?style=flat-square" alt="License" /></a>
  <img src="https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-purple?style=flat-square" alt="Target" />
</p>

---

> **v6.0.0 is here!** 9 ecosystem packages, 14-language localization, Dynamic Rule Engine, source generators, ASP.NET Core / MediatR / Blazor / gRPC / SignalR integration, and much more.
> [See what's new in this release.](CHANGELOG.md) · [What's coming next (12-month roadmap)](docs/ROADMAP.md)

---

## Why OrionGuard?

| Feature | OrionGuard | FluentValidation | Ardalis.GuardClauses | Dawn.Guard |
|---------|:----------:|:----------------:|:--------------------:|:----------:|
| Fluent guard clauses | Yes | - | Yes | Yes |
| Object validation | Yes | Yes | - | - |
| ASP.NET Core middleware | Yes | Yes | - | - |
| Minimal API filters | Yes | Yes | - | - |
| MediatR pipeline | Yes | Yes | - | - |
| Source generators | Yes | - | - | - |
| Blazor integration | Yes | Yes | - | - |
| gRPC interceptor | Yes | - | - | - |
| SignalR hub filter | Yes | - | - | - |
| OpenTelemetry | Yes | - | - | - |
| Security guards (SQL/XSS) | Yes | - | - | - |
| Dynamic JSON rules | Yes | - | - | - |
| Span-based (zero alloc) | Yes | - | - | - |
| 14 languages | Yes | 20+ | - | - |
| NativeAOT ready | Yes | - | - | - |
| Polymorphic validation | Yes | Yes | - | - |
| Deep nested validation | Yes | Yes | - | - |
| Validation caching | Yes | - | - | - |
| Domain events + outbox | Yes | - | - | - |

---

## Quick Start (30 seconds)

```bash
dotnet add package OrionGuard
```

```csharp
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Extensions;

// Guard clauses — throw on invalid
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(age).InRange(18, 120);

// High-performance — zero allocation
FastGuard.NotNullOrEmpty(name, nameof(name));
FastGuard.Email(email, nameof(email));

// Security — O(1) FrozenSet lookups
userInput.AgainstSqlInjection(nameof(userInput));
userInput.AgainstXss(nameof(userInput));

// Collect all errors — don't throw
var result = GuardResult.Combine(
    Ensure.Accumulate(email, "Email").NotNull().Email().ToResult(),
    Ensure.Accumulate(password, "Password").MinLength(8).ToResult()
);
if (result.IsInvalid)
    return BadRequest(result.ToErrorDictionary());
```

---

## Ecosystem Packages

| Package | Install | Purpose |
|---------|---------|---------|
| `OrionGuard` | `dotnet add package OrionGuard` | Core validation library |
| `OrionGuard.AspNetCore` | `dotnet add package OrionGuard.AspNetCore` | Middleware, filters, ProblemDetails, IOptions |
| `OrionGuard.MediatR` | `dotnet add package OrionGuard.MediatR` | CQRS pipeline validation |
| `OrionGuard.Generators` | `dotnet add package OrionGuard.Generators` | Compile-time source generator |
| `OrionGuard.Swagger` | `dotnet add package OrionGuard.Swagger` | OpenAPI schema generation |
| `OrionGuard.OpenTelemetry` | `dotnet add package OrionGuard.OpenTelemetry` | Metrics & tracing |
| `OrionGuard.Blazor` | `dotnet add package OrionGuard.Blazor` | EditForm validation |
| `OrionGuard.Grpc` | `dotnet add package OrionGuard.Grpc` | Server interceptor |
| `OrionGuard.SignalR` | `dotnet add package OrionGuard.SignalR` | Hub method validation |
| `OrionGuard.EntityFrameworkCore` | `dotnet add package OrionGuard.EntityFrameworkCore` | EF Core SaveChanges interceptor + transactional outbox |
| `OrionGuard.Testing` | `dotnet add package OrionGuard.Testing` | DomainEventCapture + InMemoryDispatcher + assertions |

---

## Core Features

### Fluent API

```csharp
// Automatic parameter name capture via CallerArgumentExpression
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(password).NotNull().MinLength(8);

// Transform and default
var cleaned = Ensure.That(rawEmail)
    .Transform(e => e.Trim().ToLowerInvariant())
    .Default("unknown@example.com")
    .Email()
    .Value;

// Conditional validation
Ensure.That(age)
    .When(requireAge).InRange(18, 120)
    .Unless(isAdmin).Positive()
    .Always().NotZero();
```

### Security Guards

```csharp
userInput.AgainstSqlInjection(nameof(userInput));     // 28 SQL patterns
userInput.AgainstXss(nameof(userInput));               // 28 XSS vectors
filePath.AgainstPathTraversal(nameof(filePath));       // Encoded variants
command.AgainstCommandInjection(nameof(command));      // Shell metacharacters
userInput.AgainstInjection(nameof(userInput));         // All combined
```

### International Guards (NEW in v6.0)

```csharp
"DEUTDEFF".AgainstInvalidSwiftCode(nameof(swift));     // SWIFT/BIC
"978-3-16-148410-0".AgainstInvalidIsbn(nameof(isbn));  // ISBN-10/13
"1HGCM82633A004352".AgainstInvalidVin(nameof(vin));    // Vehicle ID
"4006381333931".AgainstInvalidEan(nameof(ean));         // EAN-13
"DE123456789".AgainstInvalidVatNumber(nameof(vat));     // EU VAT
"490154203237518".AgainstInvalidImei(nameof(imei));     // IMEI
```

### Deep Nested Validation (NEW in v6.0)

```csharp
var result = Validate.Nested(order)
    .Property(o => o.OrderNumber, p => p.NotEmpty())
    .Property(o => o.Total, p => p.GreaterThan(0))
    .Nested(o => o.Customer, customer => customer
        .Property(c => c.Name, p => p.NotEmpty().Length(2, 100))
        .Property(c => c.Email, p => p.NotEmpty().Email())
        .Nested(c => c.Address, address => address
            .Property(a => a.City, p => p.NotEmpty())
            .Property(a => a.ZipCode, p => p.NotEmpty().Length(5, 10))))
    .Collection(o => o.Items, (item, index) => item
        .Property(i => i.ProductName, p => p.NotEmpty())
        .Property(i => i.Quantity, p => p.GreaterThan(0)))
    .ToResult();
// Errors: "Customer.Address.City", "Items[2].ProductName", etc.
```

### Cross-Property Validation (NEW in v6.0)

```csharp
var result = Validate.CrossProperties(booking)
    .IsGreaterThan(b => b.EndDate, b => b.StartDate, "End date must be after start date")
    .AreNotEqual(b => b.Email, b => b.Username)
    .AtLeastOneRequired(b => b.Phone, b => b.Email)
    .When(b => b.IsInternational, v => v
        .Must(b => !string.IsNullOrEmpty(b.PassportNumber), "PassportNumber", "Passport required for international bookings"))
    .ToResult();
```

### Polymorphic Validation (NEW in v6.0)

```csharp
var validator = Validate.Polymorphic<Payment>()
    .When<CreditCardPayment>(p => Validate.Nested(p)
        .Property(x => x.CardNumber, v => v.NotEmpty().Length(16, 16))
        .Property(x => x.Cvv, v => v.NotEmpty().Length(3, 4))
        .ToResult())
    .When<BankTransferPayment>(p => Validate.Nested(p)
        .Property(x => x.Iban, v => v.NotEmpty())
        .ToResult())
    .Otherwise(p => GuardResult.Failure("Payment", "Unknown payment type."));

var result = validator.Validate(payment);
```

### Dynamic Rule Engine (NEW in v6.0)

```csharp
// Load rules from JSON (e.g., from database, config file, API)
var json = """
{
  "Name": "CreateUser",
  "Rules": [
    { "PropertyName": "Email", "RuleType": "NotEmpty" },
    { "PropertyName": "Email", "RuleType": "Email" },
    { "PropertyName": "Age", "RuleType": "Range", "Parameters": { "Min": 18, "Max": 120 } },
    { "PropertyName": "Country", "RuleType": "In", "Parameters": { "Values": ["US", "UK", "TR"] } },
    { "PropertyName": "Password", "RuleType": "MinLength", "Parameters": { "Min": 8 },
      "WhenProperty": "IsNewUser", "WhenValue": true }
  ]
}
""";

var validator = DynamicValidator.FromJson(json);
var result = validator.Validate(userDto);
```

### DDD Primitives (NEW in v6.1)

> **Deprecated in v6.4.0.** The `[StronglyTypedId]` source generator is superseded by the standalone [OrionKey](https://github.com/tunahanaliozturk/OrionKey) package (`[OrionId]`). It still works through the v6.x line and is removed in v7.0.0. See [the migration guide](docs/migrations/stronglytypedid-to-orionkey.md). The manual `StronglyTypedId<TValue>` record is not affected.

```csharp
// Hybrid ValueObject — abstract class or record-based marker
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Ensure.That(amount).GreaterThanOrEqualTo(0);
        Amount = amount; Currency = currency;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount; yield return Currency;
    }
}

// Record-based value object — structural equality from the compiler
public sealed record Address(string Street, string City, string PostalCode) : IValueObject;

// Strongly-typed id via source generator (EF Core + JSON + TypeConverter all auto-generated)
[StronglyTypedId<Guid>]
public readonly partial struct OrderId;

// Aggregate root with domain events and invariant enforcement
public sealed class Order : AggregateRoot<OrderId>
{
    public Order(OrderId id) : base(id)
    {
        id.AgainstDefaultStronglyTypedId(nameof(id));
    }

    public void Ship()
    {
        CheckRule(new OrderMustBePaidRule(this));
        RaiseEvent(new OrderShippedEvent(Id));
    }
}

// Wire up DI (registers all generated EF Core converters in the calling assembly)
services.AddOrionGuardStronglyTypedIds();
```

> **v6.2 update:** `IStronglyTypedId<TValue>` marker interface unifies source-gen struct ids and manual record ids under one guard. `DomainEventBase` record spares you the `EventId`/`OccurredOnUtc` boilerplate. Generated ids implement `IParsable<TSelf>` / `ISpanParsable<TSelf>` for ASP.NET Core minimal API binding. EF Core converter emission is now conditional on the consumer referencing EF Core. Sub-package NuGet IDs dropped the `Moongazing.` prefix — install as `OrionGuard.AspNetCore`, `OrionGuard.Blazor`, etc. (C# namespaces unchanged).
>
> v6.3.0 (next) adds `IDomainEventDispatcher` + MediatR bridge + EF Core `SaveChanges` interceptor. v6.4.0 adds the `BusinessRule` base class, `Guard.Against.BrokenRule`, and ASP.NET Core ProblemDetails integration.

---

### RuleSets (NEW in v6.0)

```csharp
public class UserValidator : AbstractValidator<User>
{
    public UserValidator()
    {
        RuleFor(u => u.Email, "Email", p => p.NotEmpty().Email());

        RuleSet("create", () =>
        {
            RuleFor(u => u.Password, "Password", p => p.NotEmpty().Length(8, 100));
        });

        RuleSet("update", () =>
        {
            RuleFor(u => u.Id, "Id", p => p.NotNull());
        });
    }
}

// Execute selectively
validator.Validate(user, RuleSet.Default, RuleSet.Create);
validator.Validate(user, RuleSet.Update);
```

---

## ASP.NET Core Integration

```bash
dotnet add package OrionGuard.AspNetCore
```

```csharp
// Program.cs
builder.Services.AddOrionGuardAspNetCore();

// Minimal API with automatic validation
app.MapPost("/api/users", (CreateUserRequest req) => { ... })
   .WithValidation<CreateUserRequest>();

// IOptions validation
builder.Services.AddOptions<AppSettings>()
    .BindConfiguration("App")
    .ValidateWithOrionGuardOnStart();

// Health check
builder.Services.AddHealthChecks().AddOrionGuardCheck();

// MVC controller with attribute
[ValidateRequest]
public IActionResult Create([FromBody] CreateUserRequest request) { ... }
```

---

## MediatR Integration

```bash
dotnet add package OrionGuard.MediatR
```

```csharp
builder.Services.AddOrionGuardMediatR(typeof(Program).Assembly);

// Any IValidator<TRequest> is automatically executed before the handler
public class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email, "Email", p => p.NotEmpty().Email());
    }
}
```

---

## Source Generator (NativeAOT)

```bash
dotnet add package OrionGuard.Generators
```

```csharp
[GenerateValidator]
public sealed class CreateUserRequest
{
    [NotNull, NotEmpty, Length(3, 50)]
    public string Name { get; set; }

    [NotNull, Email]
    public string Email { get; set; }

    [Range(13, 120)]
    public int Age { get; set; }
}

// Auto-generated at compile time — no reflection!
var result = CreateUserRequestValidator.Validate(request);
```

---

## Blazor Integration

```bash
dotnet add package OrionGuard.Blazor
```

```razor
<EditForm Model="@user" OnValidSubmit="HandleSubmit">
    <OrionGuardValidator />
    <InputText @bind-Value="user.Name" />
    <ValidationMessage For="() => user.Name" />
</EditForm>
```

---

## gRPC & SignalR

```csharp
// gRPC — automatic protobuf message validation
services.AddOrionGuardGrpc();
services.AddGrpc(o => o.Interceptors.Add<OrionGuardInterceptor>());

// SignalR — automatic hub method parameter validation
services.AddOrionGuardSignalR();
```

---

## Localization (14 Languages)

English, Turkish, German, French, Spanish, Portuguese, Arabic, Japanese, Italian, Chinese, Korean, Russian, Dutch, Polish

```csharp
ValidationMessages.SetCulture("zh");
var msg = ValidationMessages.Get("NotNull", "Email");
// Output: "Email 不能为空。"
```

---

## Performance

- **GeneratedRegex** — All 24 regex patterns are source-generated. Zero runtime compilation.
- **FastGuard** — Span-based zero-allocation validation with `[MethodImpl(AggressiveInlining)]`
- **FrozenSet** — O(1) lookups for security patterns (SQL, XSS, path traversal)
- **ThrowHelper** — `[DoesNotReturn]` + `[StackTraceHidden]` for minimal JIT footprint
- **Validation Caching** — Cache results with TTL for identical inputs
- **NativeAOT** — Source generator enables reflection-free validation
- **AOT story for v6.3.0 domain events:** `ServiceProviderDomainEventDispatcher` and `OutboxDispatcherHostedService` use runtime reflection; they are marked with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. Two AOT-friendly paths exist: (1) use the **MediatR bridge** (`MediatRDomainEventDispatcher`) which has no reflection, or (2) root your event/handler types via `[DynamicDependency]` and use System.Text.Json source generation for outbox payloads. The core guard / validation surface remains fully AOT-safe.

---

## Benchmarks

Measured with BenchmarkDotNet v0.14.0 on an Intel Core i7-7820HQ CPU 2.90GHz (Kaby Lake), Windows 11, .NET 10.0.5 (X64 RyuJIT AVX2). Run with the `ShortRun` job (3 warmup + 3 iterations); numbers are indicative. Run `dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks` to reproduce.

### Null checks

| Method                               | Mean       | Error      | StdDev    | Median     | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------------- |-----------:|-----------:|----------:|-----------:|-------:|--------:|-------:|----------:|------------:|
| RawIfThrow_NotNull                   |  0.9427 ns |  9.0955 ns | 0.4986 ns |  0.7322 ns |  1.175 |    0.72 |      - |         - |          NA |
| Guard_AgainstNull                    |  0.1294 ns |  0.8487 ns | 0.0465 ns |  0.1418 ns |  0.161 |    0.08 |      - |         - |          NA |
| Ensure_That_NotNull                  | 15.1543 ns | 34.4373 ns | 1.8876 ns | 14.1039 ns | 18.890 |    7.35 | 0.0191 |      80 B |          NA |
| FastGuard_NotNull                    |  1.2557 ns |  1.3515 ns | 0.0741 ns |  1.2389 ns |  1.565 |    0.59 |      - |         - |          NA |
| Guard_AgainstNullOrEmpty_ValidString |  1.1358 ns |  3.1390 ns | 0.1721 ns |  1.0534 ns |  1.416 |    0.56 |      - |         - |          NA |
| FastGuard_NotNullOrEmpty_ValidString |  0.0796 ns |  1.7406 ns | 0.0954 ns |  0.0534 ns |  0.099 |    0.12 |      - |         - |          NA |
| Ensure_NotNullOrEmpty_ValidString    |  0.0000 ns |  0.0000 ns | 0.0000 ns |  0.0000 ns |  0.000 |    0.00 |      - |         - |          NA |

### Email validation

| Method                    | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| RawCompiledRegex_Email    | 78.23 ns | 23.68 ns | 1.298 ns |  1.00 |    0.02 |      - |         - |          NA |
| GeneratedRegex_Email      | 79.45 ns | 34.13 ns | 1.871 ns |  1.02 |    0.03 |      - |         - |          NA |
| Guard_AgainstInvalidEmail | 74.11 ns | 28.21 ns | 1.546 ns |  0.95 |    0.02 |      - |         - |          NA |
| FastGuard_Email_SpanBased | 11.66 ns | 19.49 ns | 1.068 ns |  0.15 |    0.01 |      - |         - |          NA |
| Ensure_That_Email         | 84.45 ns | 33.18 ns | 1.819 ns |  1.08 |    0.03 | 0.0191 |      80 B |          NA |

### Regex patterns

| Method                      | Mean      | Error     | StdDev   | Gen0   | Allocated |
|---------------------------- |----------:|----------:|---------:|-------:|----------:|
| RegexCache_Email            | 155.53 ns | 51.732 ns | 2.836 ns | 0.0191 |      80 B |
| GeneratedRegex_Email        |  75.65 ns | 30.528 ns | 1.673 ns |      - |         - |
| RegexCache_PhoneNumber      | 120.45 ns | 10.215 ns | 0.560 ns | 0.0153 |      64 B |
| GeneratedRegex_PhoneNumber  |  50.09 ns | 56.275 ns | 3.085 ns |      - |         - |
| RegexCache_AlphaNumeric     | 104.81 ns | 87.034 ns | 4.771 ns | 0.0134 |      56 B |
| GeneratedRegex_AlphaNumeric |  35.11 ns |  6.788 ns | 0.372 ns |      - |         - |

### Object validation

| Method                            | Mean         | Error        | StdDev     | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------- |-------------:|-------------:|-----------:|-------:|--------:|-------:|----------:|------------:|
| ManualValidation                  |     4.786 ns |     1.316 ns |  0.0721 ns |   1.00 |    0.02 |      - |         - |          NA |
| Validate_For_AllProperties        | 2,546.446 ns |   542.276 ns | 29.7240 ns | 532.11 |    8.78 | 0.8850 |    3704 B |          NA |
| Validate_For_WithPropertyChaining | 2,777.566 ns | 1,464.866 ns | 80.2942 ns | 580.41 |   16.38 | 0.8545 |    3576 B |          NA |
| Validate_ForStrict_AllProperties  | 2,642.425 ns | 1,409.270 ns | 77.2468 ns | 552.17 |   15.72 | 0.8698 |    3640 B |          NA |

### Security guards

| Method                     | Mean         | Error        | StdDev     | Allocated |
|--------------------------- |-------------:|-------------:|-----------:|----------:|
| AgainstSqlInjection_Short  |     41.98 ns |     6.435 ns |   0.353 ns |         - |
| AgainstSqlInjection_Medium |  1,266.87 ns |   607.982 ns |  33.326 ns |         - |
| AgainstSqlInjection_Long   | 25,233.83 ns | 2,297.883 ns | 125.955 ns |         - |
| AgainstXss_Short           |     34.03 ns |    31.848 ns |   1.746 ns |         - |
| AgainstXss_Medium          |    297.09 ns |   217.671 ns |  11.931 ns |         - |
| AgainstXss_Long            |  3,230.04 ns | 1,115.045 ns |  61.119 ns |         - |
| AgainstInjection_Short     |    129.59 ns |    29.715 ns |   1.629 ns |         - |
| AgainstInjection_Medium    |  1,735.31 ns |   451.942 ns |  24.772 ns |         - |

### Domain primitives

| Method                     | Mean       | Error      | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |-----------:|-----------:|----------:|------:|--------:|-------:|----------:|------------:|
| ValueObject_ClassEquality  | 104.908 ns | 66.1232 ns | 3.6244 ns |  1.00 |    0.04 | 0.0343 |     144 B |        1.00 |
| ValueObject_RecordEquality |   6.187 ns |  0.9568 ns | 0.0524 ns |  0.06 |    0.00 |      - |         - |        0.00 |
| AggregateRoot_RaiseAndPull | 160.362 ns | 25.2494 ns | 1.3840 ns |  1.53 |    0.05 | 0.0172 |      72 B |        0.50 |

---

## FluentValidation Migration

OrionGuard provides a compatibility layer for easy migration:

```csharp
// Change: using FluentValidation;
// To:     using Moongazing.OrionGuard.Compatibility;

public class UserValidator : FluentStyleValidator<User>
{
    public UserValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Age).InclusiveBetween(18, 120);
    }
}
```

---

## Custom Exception Factory

```csharp
public class MyExceptionFactory : IExceptionFactory
{
    public Exception CreateException(string errorCode, string parameterName, string message, Exception? inner)
        => new MyCustomException(errorCode, parameterName, message);
}

services.AddOrionGuardExceptionFactory<MyExceptionFactory>();
// Or globally: ExceptionFactoryProvider.Configure(new MyExceptionFactory());
```

---

## Roadmap

OrionGuard publishes a public, twelve-month forward roadmap covering the next minor releases
through **v7.0.0 (Q2 2027)**. See [docs/ROADMAP.md](docs/ROADMAP.md) for the next-12-months
view (v6.5.0 family integration, v6.6.0 migration tooling, v6.7.0 production excellence,
v7.0.0 API freeze) plus the deep tier backlog of every item under consideration.

If something on the roadmap matters to you, open an issue with the `roadmap` label. Real
workload demand is what moves items up the list.

---

## More from the Orion family

OrionGuard is one of a set of standalone .NET libraries:

- [OrionAudit](https://github.com/tunahanaliozturk/OrionAudit) - automatic EF Core change-audit trail.
- [OrionKey](https://github.com/tunahanaliozturk/OrionKey) - source-generated strongly-typed IDs.
- [OrionLock](https://github.com/tunahanaliozturk/OrionLock) - distributed locking.
- [OrionPatch](https://github.com/tunahanaliozturk/OrionPatch) - transactional outbox for EF Core (enqueue inside SaveChanges, dispatch at-least-once through a pluggable sink).
- [OrionVault](https://github.com/tunahanaliozturk/OrionVault) - column-level transparent data encryption at rest for EF Core.

---

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) before submitting a pull request.

## License

This project is licensed under the [MIT License](src/Moongazing.OrionGuard/docs/LICENSE.txt).

## Author

**Tunahan Ali Ozturk** - [GitHub](https://github.com/tunahanaliozturk)
