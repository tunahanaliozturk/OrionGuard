# Getting started

Install the core package:

```bash
dotnet add package OrionGuard
```

Everything on this page is in that package. The integration packages come later, on
[the packages pages](../packages/index.md).

## 1. Guard a method's inputs

Guards throw on the first failure. Use them where a method must refuse to run at all, such as a
constructor, a domain method, or the top of a handler.

```csharp
using Moongazing.OrionGuard.Core;

public sealed class Payment
{
    public Payment(string reference, decimal amount, string displayName)
    {
        // Ensure.That captures the parameter name through CallerArgumentExpression, so the
        // exception says which argument was wrong without repeating its name here.
        Ensure.That(reference).NotNull().NotEmpty().MaxLength(32);
        Ensure.That(amount).GreaterThan(0m);

        // FastGuard is the span-based version for hot paths; it takes the name explicitly.
        FastGuard.NotNullOrEmpty(displayName, nameof(displayName));

        Reference = reference;
        Amount = amount;
        DisplayName = displayName;
    }

    public string Reference { get; }
    public decimal Amount { get; }
    public string DisplayName { get; }
}
```

`Ensure` and `Guard` throw `GuardException` or one of its subclasses (`NullValueException`,
`OutOfRangeException`, ...). The extension guards in `Moongazing.OrionGuard.Extensions` mostly throw
`ArgumentException`. To surface your own exception types, catch these at your boundary and rethrow.

## 2. Collect every error instead of throwing

`Ensure.Accumulate` runs every rule and returns a `GuardResult`. `GuardResult.Combine` merges
results, and `ToErrorDictionary()` produces the `{ field: [messages] }` shape that
`Results.ValidationProblem` expects.

```csharp
using Moongazing.OrionGuard.Core;

public static class SignUpCheck
{
    public static Dictionary<string, string[]>? Run(string email, string password)
    {
        GuardResult result = GuardResult.Combine(
            Ensure.Accumulate(email).NotNull().Email().ToResult(),
            Ensure.Accumulate(password).NotNull().MinLength(8).ToResult());

        // Errors carry a Severity; IsInvalid is true only for Severity.Error entries.
        return result.IsInvalid ? result.ToErrorDictionary() : null;
    }
}
```

## 3. Validate a whole object

`Validate.For(instance)` describes rules per property and returns one result. Rules that need I/O
(a uniqueness check, say) are added with `MustAsync`.

```csharp
using Moongazing.OrionGuard.Core;

public sealed record CreateUser(string Email, string Password, int Age);

public interface IUserRepository
{
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);
}

public sealed class CreateUserCheck(IUserRepository users)
{
    public Task<GuardResult> RunAsync(CreateUser input, CancellationToken cancellationToken) =>
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

Once a validator has a `MustAsync` rule, finish with an async terminal (`ToResultAsync`,
`BuildAsync`, `ThrowIfInvalidAsync`). The synchronous `ToResult()` throws
`InvalidOperationException` rather than quietly skipping the async rules.

For deep object graphs use `Validate.Nested(obj)`, for rules that span properties
`Validate.CrossProperties(obj)`, and for per-subtype rules `Validate.Polymorphic<T>()`.

## 4. Make it a reusable validator

A validator class is what the integration packages resolve. Derive from `AbstractValidator<T>`,
which supports sync rules, async rules and named rule sets.

```csharp
using Moongazing.OrionGuard.DependencyInjection;

public sealed record CreateUserRequest(string Email, string Password);

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email, nameof(CreateUserRequest.Email), p => p.NotEmpty().Email());
        RuleFor(x => x.Password, nameof(CreateUserRequest.Password), p => p.NotEmpty().Length(8, 128));
    }
}
```

Register it once:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

public static class ValidationRegistration
{
    public static IServiceCollection AddValidation(this IServiceCollection services) =>
        services
            .AddOrionGuard()
            .AddValidator<CreateUserRequest, CreateUserRequestValidator>();
}
```

`AddOrionGuard()` registers the `IValidatorFactory`; `AddValidator<T, TValidator>()` registers the
validator as a transient `IValidator<T>`. Nothing scans your assemblies, so each validator is
registered explicitly.

## 5. Return it from ASP.NET Core

```bash
dotnet add package OrionGuard.AspNetCore
```

```csharp
using Moongazing.OrionGuard.AspNetCore.Extensions;
using Moongazing.OrionGuard.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrionGuardAspNetCore();
builder.Services.AddValidator<CreateUserRequest, CreateUserRequestValidator>();

var app = builder.Build();

// Maps OrionGuard exceptions to RFC 9457 ProblemDetails responses.
app.UseOrionGuardValidation();

app.MapPost("/users", (CreateUserRequest request) => Results.Ok(request))
   .WithValidation<CreateUserRequest>();

app.Run();
```

A request that fails validation never reaches the handler: the endpoint filter answers with
`ValidationProblemDetails` and status 422 by default. MVC controllers use `[ValidateRequest]`
instead. Both are described on the
[OrionGuard.AspNetCore page](../packages/aspnetcore.md).

## 6. Messages in another language

Bundled messages exist in 14 languages. Set the culture for the current scope (a request, a job)
rather than globally:

```csharp
using System.Globalization;
using Moongazing.OrionGuard.Localization;

public static class Culture
{
    public static void UseTurkishForThisRequest() =>
        ValidationMessages.SetCultureForCurrentScope(new CultureInfo("tr"));
}
```

## Where next

- [Security guards](security-guards.md) — the injection heuristics, and what they do not replace.
- [Packages](../packages/index.md) — MediatR, Blazor, gRPC, SignalR, Hangfire, EF Core, OpenTelemetry.
- [Playground](../playground/index.html) — try rules and guards without installing anything.
- [API reference](../api/index.md) — every public type, from the XML docs.
