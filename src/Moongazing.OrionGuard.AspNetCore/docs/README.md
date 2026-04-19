# Moongazing.OrionGuard.AspNetCore

ASP.NET Core integration package for [**OrionGuard**](https://github.com/tunahanaliozturk/OrionGuard) — the modern, fluent, and extensible validation ecosystem for .NET.

## What this package adds

- **Middleware** that converts `GuardException` and validation failures into RFC 9457 `ProblemDetails` responses.
- **Minimal API endpoint filter** — `.WithValidation<TRequest>()` runs your OrionGuard validators before the handler executes.
- **MVC action filter** — `[ValidateRequest]` attribute for controller actions.
- **IOptions validation** — `services.AddOptions<T>().ValidateWithOrionGuardOnStart()` validates configuration at startup so misconfigured apps fail fast.
- **Health check** integration — `AddOrionGuardCheck()` surfaces the validation subsystem in `/health`.

## Install

```bash
dotnet add package Moongazing.OrionGuard.AspNetCore
```

The core `OrionGuard` package is brought in transitively; you do not need to install it separately.

## Quick start

```csharp
using Moongazing.OrionGuard.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOrionGuardAspNetCore();

var app = builder.Build();

// Minimal API
app.MapPost("/users", (CreateUserRequest req) => Results.Ok(req))
   .WithValidation<CreateUserRequest>();

// MVC
[ValidateRequest]
public IActionResult Create([FromBody] CreateUserRequest request) => Ok();

// IOptions
builder.Services.AddOptions<AppSettings>()
    .BindConfiguration("App")
    .ValidateWithOrionGuardOnStart();

app.Run();
```

## Targets

.NET 8.0, .NET 9.0, .NET 10.0.

## License

MIT. See the [main repository](https://github.com/tunahanaliozturk/OrionGuard) for full docs, CHANGELOG, and samples.
