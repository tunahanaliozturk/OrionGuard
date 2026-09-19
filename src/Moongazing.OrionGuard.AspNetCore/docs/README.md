# OrionGuard.AspNetCore

ASP.NET Core integration for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). It maps OrionGuard exceptions to RFC 9457 `ProblemDetails` responses, validates Minimal API requests with an endpoint filter, and validates bound options at startup.

## Install

```bash
dotnet add package OrionGuard.AspNetCore
```

The core `OrionGuard` package is installed as a dependency.

## Quick start

```csharp
using Moongazing.OrionGuard.AspNetCore.Extensions;
using Moongazing.OrionGuard.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrionGuardAspNetCore();
builder.Services.AddValidator<CreateUserRequest, CreateUserValidator>();

var app = builder.Build();

app.UseOrionGuardValidation(); // adds app.UseExceptionHandler()

app.MapPost("/users", (CreateUserRequest request) => Results.Ok(request))
   .WithValidation<CreateUserRequest>();

app.Run();

public sealed record CreateUserRequest(string Email);

public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email, nameof(CreateUserRequest.Email), p => p.NotEmpty().Email());
    }
}
```

`AddOrionGuardAspNetCore(Action<OrionGuardAspNetCoreOptions>? configure = null)` calls `AddOrionGuard()`, registers the options as a singleton, and registers `OrionGuardExceptionHandler` with `AddExceptionHandler` and `AddProblemDetails()`. It does not scan assemblies, so register each validator with `AddValidator<T, TValidator>()`.

## Options

| Option | Default | Effect |
| --- | --- | --- |
| `UseProblemDetails` | `true` | When `false`, the exception handler and endpoint filter write plain JSON instead of `ProblemDetails` |
| `DefaultStatusCode` | `422` | Status for endpoint-filter failures and `AggregateValidationException` |
| `BusinessRuleStatusCode` | `422` | Status for `BusinessRuleValidationException` (set to 400 for clients that expect it) |
| `SuppressModelStateInvalidFilter` | `false` | Turns off MVC's automatic 400 response for invalid model state |

## Exception mapping

`OrionGuardExceptionHandler` is an `IExceptionHandler`. It runs when `UseOrionGuardValidation()` or `UseExceptionHandler()` is in the pipeline.

| Exception | Status | Response |
| --- | --- | --- |
| `AggregateValidationException` (for example from `GuardResult.ThrowIfInvalid()` or OrionGuard.MediatR) | `DefaultStatusCode` | `ValidationProblemDetails`, type `https://tools.ietf.org/html/rfc9457`, title `Validation Failed`, `errors` keyed by parameter name |
| `BusinessRuleValidationException` | `BusinessRuleStatusCode` | `ValidationProblemDetails`, type `https://moongazing.dev/orionguard/problems/business-rule-violation`, title `Business Rule Violation`, `errors` keyed by the rule's type name |
| `GuardException` | 400 | `ValidationProblemDetails`, title `Validation Failed`, `errors` keyed by parameter name |

Other exceptions are not handled here and go on to the next handler. With `UseProblemDetails = false`, the three bodies are `{ "errors": [{ "parameterName", "message" }] }`, `{ "ruleName", "message" }`, and `{ "error", "parameterName" }`.

## Minimal API endpoint filter

`.WithValidation<TRequest>()` adds `OrionGuardEndpointFilter<TRequest>` to the route handler. The filter resolves `IValidator<TRequest>` from the request services and finds the first handler argument of type `TRequest`. It then runs `ValidateAsync`, so async rules run too. On failure it returns `ValidationProblemDetails` with `DefaultStatusCode`. If no validator is registered, or no argument matches, the handler runs without validation.

## Options validation

```csharp
builder.Services.AddOptions<SmtpSettings>()
    .BindConfiguration("Smtp")
    .ValidateWithOrionGuardOnStart();
```

The options are checked against their OrionGuard attributes (`[NotNull]`, `[NotEmpty]`, `[Length]`, `[Email]`, `[Range]`, `[Regex]`, `[Positive]` from `Moongazing.OrionGuard.Attributes`) and, if one is registered, an `IValidator<TOptions>`. `ValidateWithOrionGuard()` does the same without the startup check, so the options are validated on first access.

## Health check

```csharp
builder.Services.AddHealthChecks().AddOrionGuardCheck(); // name "orionguard", tags "validation", "orionguard"
app.MapHealthChecks("/health");
```

The check reports `Degraded` when `IValidatorFactory` is not registered, and `Healthy` otherwise.

## Known issue: `[ValidateRequest]`

The package contains `[ValidateRequest]` and `OrionGuardMvcFilter` for MVC controllers, but they do not work in this version. `AddOrionGuardAspNetCore()` registers the filter in DI without adding it to the MVC filter pipeline. If you add it yourself, it throws `AmbiguousMatchException` for any action argument that has a registered validator. For controllers, call the validator in the action, or call `ThrowIfInvalid()` on the result and let the exception handler produce the response.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- Uses the ASP.NET Core shared framework (`Microsoft.AspNetCore.App`); no other package dependencies

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR), [OrionGuard.Swagger](https://www.nuget.org/packages/OrionGuard.Swagger), [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
