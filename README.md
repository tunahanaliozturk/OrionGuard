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
> [See what's new in this release.](CHANGELOG.md)

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
| `Moongazing.OrionGuard.AspNetCore` | `dotnet add package Moongazing.OrionGuard.AspNetCore` | Middleware, filters, ProblemDetails, IOptions |
| `Moongazing.OrionGuard.MediatR` | `dotnet add package Moongazing.OrionGuard.MediatR` | CQRS pipeline validation |
| `Moongazing.OrionGuard.Generators` | `dotnet add package Moongazing.OrionGuard.Generators` | Compile-time source generator |
| `Moongazing.OrionGuard.Swagger` | `dotnet add package Moongazing.OrionGuard.Swagger` | OpenAPI schema generation |
| `Moongazing.OrionGuard.OpenTelemetry` | `dotnet add package Moongazing.OrionGuard.OpenTelemetry` | Metrics & tracing |
| `Moongazing.OrionGuard.Blazor` | `dotnet add package Moongazing.OrionGuard.Blazor` | EditForm validation |
| `Moongazing.OrionGuard.Grpc` | `dotnet add package Moongazing.OrionGuard.Grpc` | Server interceptor |
| `Moongazing.OrionGuard.SignalR` | `dotnet add package Moongazing.OrionGuard.SignalR` | Hub method validation |

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

> v6.2.0 will add `IDomainEventDispatcher` + MediatR bridge + EF Core `SaveChanges` interceptor. v6.3.0 will add the `BusinessRule` base class, `Guard.Against.BrokenRule`, `Validate.Rule`, and ASP.NET Core ProblemDetails mapping.

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
dotnet add package Moongazing.OrionGuard.AspNetCore
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
dotnet add package Moongazing.OrionGuard.MediatR
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
dotnet add package Moongazing.OrionGuard.Generators
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
dotnet add package Moongazing.OrionGuard.Blazor
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

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) before submitting a pull request.

## License

This project is licensed under the [MIT License](src/Moongazing.OrionGuard/docs/LICENSE.txt).

## Author

**Tunahan Ali Ozturk** - [GitHub](https://github.com/tunahanaliozturk)
