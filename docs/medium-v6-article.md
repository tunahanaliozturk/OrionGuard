# How I Built the Largest Validation Ecosystem in .NET — And What I Learned

*From a single guard clause library to 9 NuGet packages, 457 tests, and 14 languages*

---

If you've ever written this code:

```csharp
if (email == null) throw new ArgumentNullException(nameof(email));
if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required");
if (!IsValidEmail(email)) throw new ArgumentException("Invalid email");
if (age < 18 || age > 120) throw new ArgumentOutOfRangeException(nameof(age));
```

...you know the pain. Four guards. Four throw statements. Zero business logic. Now multiply this across 200 endpoints and 50 services.

A year ago, I published OrionGuard v1.0 with a simple idea: **validation should be one line, not ten.**

```csharp
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(age).InRange(18, 120);
```

Today, OrionGuard v6.0 is something much bigger. It's not just a guard clause library anymore. It's a **complete validation ecosystem** for .NET.

Here's the story of how it got there, what I learned, and why I think every .NET developer should care.

---

## The Problem Nobody Was Solving

When I looked at the .NET validation landscape, I saw fragmentation:

- **FluentValidation** is great for object validation, but has no guard clauses, no security checks, no span-based performance path.
- **Ardalis.GuardClauses** is great for simple null checks, but stops there. No ASP.NET Core middleware. No MediatR integration. No localization.
- **Dawn.Guard** is minimal by design. That's fine, but not enough for enterprise applications.

Every project I worked on needed bits from multiple libraries. I wanted **one ecosystem** that covers everything:

| Need | Before | OrionGuard v6.0 |
|------|--------|-----------------|
| Null check | `if (x == null) throw` | `Ensure.That(x).NotNull()` |
| SQL injection | Manual regex | `input.AgainstSqlInjection()` |
| IBAN validation | Stack Overflow copy-paste | `iban.AgainstInvalidIban()` |
| API validation | FluentValidation + middleware | `.WithValidation<T>()` |
| MediatR pipeline | Separate package | `AddOrionGuardMediatR()` |
| Blazor forms | DataAnnotationsValidator | `<OrionGuardValidator />` |
| Runtime rules | Build your own | `DynamicValidator.FromJson(json)` |
| gRPC validation | Nothing existed | `OrionGuardInterceptor` |

---

## What's in v6.0?

### 9 NuGet Packages

OrionGuard is no longer a single package. It's an ecosystem:

```
OrionGuard                          Core library
Moongazing.OrionGuard.AspNetCore    Middleware, filters, ProblemDetails
Moongazing.OrionGuard.MediatR       CQRS pipeline validation
Moongazing.OrionGuard.Generators    Compile-time source generator
Moongazing.OrionGuard.Swagger       OpenAPI schema from attributes
Moongazing.OrionGuard.OpenTelemetry Metrics and distributed tracing
Moongazing.OrionGuard.Blazor        EditForm validation components
Moongazing.OrionGuard.Grpc          Server interceptor
Moongazing.OrionGuard.SignalR        Hub method validation
```

**Why separate packages?** Because a Blazor developer shouldn't need to download gRPC dependencies, and a console app shouldn't pull in ASP.NET Core. Each package adds a transitive download of the core, which multiplies total NuGet downloads.

### Source Generator: Zero Reflection, NativeAOT Ready

This is the feature I'm most proud of. Write this:

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
```

The Roslyn source generator emits a `CreateUserRequestValidator.Validate()` method **at compile time**. No reflection. No runtime overhead. Works with NativeAOT. This is the future of .NET validation.

### Dynamic Rule Engine: JSON-Configured Validation

For multi-tenant SaaS applications, hardcoded validation doesn't work. Different customers need different rules.

```json
{
  "Name": "CreateUser",
  "Rules": [
    { "PropertyName": "Email", "RuleType": "NotEmpty" },
    { "PropertyName": "Email", "RuleType": "Email" },
    { "PropertyName": "Age", "RuleType": "Range",
      "Parameters": { "Min": 18, "Max": 120 } },
    { "PropertyName": "Country", "RuleType": "In",
      "Parameters": { "Values": ["US", "UK", "TR"] } }
  ]
}
```

```csharp
var validator = DynamicValidator.FromJson(json);
var result = validator.Validate(userDto);
```

Load rules from a database, config file, or API. Change them without redeploying.

### Deep Nested Validation

One of the most requested features. Validate entire object graphs with full path tracking:

```csharp
var result = Validate.Nested(order)
    .Property(o => o.Total, p => p.GreaterThan(0))
    .Nested(o => o.Customer, customer => customer
        .Property(c => c.Email, p => p.NotEmpty().Email())
        .Nested(c => c.Address, address => address
            .Property(a => a.City, p => p.NotEmpty())))
    .Collection(o => o.Items, (item, idx) => item
        .Property(i => i.Quantity, p => p.GreaterThan(0)))
    .ToResult();
// Error paths: "Customer.Address.City", "Items[2].Quantity"
```

### 14 Languages

OrionGuard now speaks English, Turkish, German, French, Spanish, Portuguese, Arabic, Japanese, Italian, Chinese, Korean, Russian, Dutch, and Polish. Every one of the 30 message keys is translated in all 14 languages — 420 messages total.

### Performance That Matters

All 24 regex patterns use `[GeneratedRegex]` — source-generated at compile time, not compiled at runtime. `FastGuard` uses `ReadOnlySpan<char>` for zero-allocation email, GUID, and ASCII validation. Security guards use `FrozenSet<string>` for O(1) pattern matching.

---

## Architecture Decisions That Shaped v6.0

### 1. Keep the Core Dependency-Free

The core `OrionGuard` package has exactly **one** dependency: `Microsoft.Extensions.DependencyInjection.Abstractions`. That's it. No ASP.NET Core. No MediatR. No OpenTelemetry. Every integration is a separate package that users opt into.

### 2. Backward Compatibility Is Non-Negotiable

Every v5.0 API still works in v6.0. `Guard.AgainstNull()`, `Ensure.That()`, `FastGuard.NotNull()` — all unchanged. The only deprecation is `RegexPatterns` (replaced by `GeneratedRegexPatterns`), and it still compiles with a warning.

### 3. Don't Fight the Framework

Instead of building custom middleware, OrionGuard uses `IEndpointFilter` for Minimal APIs, `IAsyncActionFilter` for MVC, `IExceptionHandler` for error handling, and `IValidateOptions<T>` for configuration validation. Standard ASP.NET Core patterns, not custom abstractions.

### 4. Make Every Error Actionable

`GuardResult.ToErrorDictionary()` produces the exact format ASP.NET Core's `ValidationProblemDetails` expects. Nested validation paths like `"Customer.Address.City"` and `"Items[2].Quantity"` tell the frontend exactly which field has the problem.

---

## The Numbers

| Metric | Value |
|--------|-------|
| Projects in solution | 12 |
| NuGet packages | 9 |
| Unit tests | 457 (all passing) |
| Languages supported | 14 |
| Target frameworks | .NET 8, 9, 10 |
| GeneratedRegex patterns | 24 |
| Security patterns (FrozenSet) | 80+ |
| Guard methods | 100+ |
| Dynamic rule types | 14 |
| Lines of code | ~15,000 |

---

## What I Learned Building This

### Open source is a multiplier
OrionGuard started as a utility for my own projects. Publishing it forced me to think about API design, backward compatibility, documentation, and developer experience in ways that made me a better engineer.

### Ecosystem > Features
A library with 100 features and no integrations gets fewer downloads than a library with 20 features and ASP.NET Core middleware. **Integration is distribution.**

### Tests are documentation
The 457 tests in OrionGuard aren't just for correctness. They're the most accurate documentation of how every feature works. When someone asks "how does DynamicValidator handle conditional rules?", I point them to `DynamicRuleEngineTests.cs`.

### Performance claims need proof
That's why OrionGuard ships with a BenchmarkDotNet suite. "Fast" is a marketing claim. "3ns per null check with zero allocations" is a fact.

---

## What's Next?

OrionGuard v6.0 is the foundation. Here's what's coming:

- **OrionGuard Studio** — A visual dashboard for managing Dynamic Rule Engine configurations
- **More Roslyn Analyzers** — IDE suggestions like "this null check could use Ensure.That()"
- **Template Pack** — `dotnet new orionguard-api` for instant project scaffolding

---

## Get Started

```bash
dotnet add package OrionGuard
```

For the full ecosystem:
```bash
dotnet add package Moongazing.OrionGuard.AspNetCore
dotnet add package Moongazing.OrionGuard.MediatR
dotnet add package Moongazing.OrionGuard.Generators
```

- **GitHub:** [github.com/Moongazing/OrionGuard](https://github.com/Moongazing/OrionGuard)
- **NuGet:** [nuget.org/packages/OrionGuard](https://www.nuget.org/packages/OrionGuard)
- **Changelog:** [CHANGELOG.md](https://github.com/Moongazing/OrionGuard/blob/master/CHANGELOG.md)

---

*If OrionGuard saves you time, consider giving it a star on GitHub. It helps more than you think.*

---

**Tags:** #dotnet #csharp #validation #opensource #aspnetcore #nuget #blazor #grpc #mediatr
