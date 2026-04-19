# Moongazing.OrionGuard.MediatR

MediatR integration for [**OrionGuard**](https://github.com/tunahanaliozturk/OrionGuard) — runs your OrionGuard validators automatically inside the MediatR pipeline before each request handler.

## What this package adds

- **`ValidationBehavior<TRequest, TResponse>`** — a MediatR pipeline behavior that locates any `IValidator<TRequest>` registered in DI and executes it before the handler.
- **`AddOrionGuardMediatR(...)`** — a service-collection extension that registers the behavior and scans the supplied assemblies for validators.
- **CQRS-friendly** — zero reflection overhead after the first request thanks to MediatR's own DI resolution.

## Install

```bash
dotnet add package Moongazing.OrionGuard.MediatR
```

Requires MediatR in your application; the core `OrionGuard` package is brought in transitively.

## Quick start

```csharp
using Moongazing.OrionGuard.MediatR;

builder.Services.AddOrionGuardMediatR(typeof(Program).Assembly);

// Any IValidator<TRequest> is executed automatically before its handler:
public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email, "Email", p => p.NotEmpty().Email());
        RuleFor(x => x.Password, "Password", p => p.NotEmpty().MinLength(8));
    }
}
```

When validation fails, the pipeline short-circuits with a `ValidationException` carrying the full `GuardResult` — you can convert it to `ProblemDetails` with the companion `Moongazing.OrionGuard.AspNetCore` package.

## Targets

.NET 8.0, .NET 9.0, .NET 10.0.

## License

MIT. See the [main repository](https://github.com/tunahanaliozturk/OrionGuard) for full docs, CHANGELOG, and samples.
