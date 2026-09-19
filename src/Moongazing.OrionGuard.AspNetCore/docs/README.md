# OrionGuard.AspNetCore

Turns OrionGuard validation into HTTP responses: an RFC 9457 `ProblemDetails` body for every OrionGuard exception, a filter that validates Minimal API and MVC requests before the handler runs, and a startup check for bound options.

```bash
dotnet add package OrionGuard.AspNetCore
```

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

`POST /users` with `{"email":"not-an-email"}` answers `422 Unprocessable Content` with

```json
{
  "type": "https://tools.ietf.org/html/rfc9457",
  "title": "Validation Failed",
  "status": 422,
  "errors": { "Email": ["..."] }
}
```

and the handler never runs. The core `OrionGuard` package comes along as a dependency.

## Registration

`AddOrionGuardAspNetCore(Action<OrionGuardAspNetCoreOptions>? configure = null)` calls `AddOrionGuard()`, registers the options as a singleton, registers `OrionGuardExceptionHandler` through `AddExceptionHandler` together with `AddProblemDetails()`, and registers `OrionGuardMvcFilter`. It does not scan assemblies: register each validator yourself with `AddValidator<T, TValidator>()`.

`UseOrionGuardValidation()` adds `UseExceptionHandler()`. If your pipeline already calls `UseExceptionHandler()`, the handler is picked up there and this call is redundant.

## Validate a Minimal API endpoint

`.WithValidation<TRequest>()` adds `OrionGuardEndpointFilter<TRequest>`. It resolves `IValidator<TRequest>` from the request services, finds the first handler argument of that type, and runs `ValidateAsync`, so async rules run as well. A failure answers with the status the validator suggested through `GuardResult.FailureWithStatus` — say 409 for a conflict — and falls back to `DefaultStatusCode` when the validator suggested none.

## Validate an MVC action

```csharp
using Microsoft.AspNetCore.Mvc;
using Moongazing.OrionGuard.AspNetCore.Attributes;

public sealed record CreateUser(string Email);

[ApiController]
[Route("users")]
public sealed class UsersController : ControllerBase
{
    [HttpPost]
    [ValidateRequest]
    public IActionResult Create(CreateUser request) => Ok(request);
}
```

`[ValidateRequest]` goes on a controller or a single action and adds `OrionGuardMvcFilter` to that action's pipeline — there is no global filter to register.

- For each non-null action argument it runs *every* `IValidator<T>` registered for the argument's runtime type, resolved from the request services, so scoped validators (one holding a `DbContext`, for instance) work.
- Validators run one after another through `ValidateAsync`, so `RuleForAsync` rules run and two validators never share a `DbContext` concurrently.
- The first invalid argument stops the action. The status is the validator's suggestion, else `DefaultStatusCode`.
- Putting the attribute on both the controller and the action still validates once per request.

## Map exceptions to ProblemDetails

`OrionGuardExceptionHandler` is an `IExceptionHandler`, so it needs `UseOrionGuardValidation()` or `UseExceptionHandler()` in the pipeline.

| Exception | Status | Body |
| --- | --- | --- |
| `AggregateValidationException` — from `GuardResult.ThrowIfInvalid()`, or from OrionGuard.MediatR | `DefaultStatusCode` | `ValidationProblemDetails`, type `https://tools.ietf.org/html/rfc9457`, title `Validation Failed`, `errors` keyed by parameter name |
| `BusinessRuleValidationException` | `BusinessRuleStatusCode` | `ValidationProblemDetails`, type `https://moongazing.dev/orionguard/problems/business-rule-violation`, title `Business Rule Violation`, `errors` keyed by the rule's type name |
| `GuardException` | 400 | `ValidationProblemDetails`, title `Validation Failed`, `errors` keyed by parameter name |

Anything else falls through to the next handler. With `UseProblemDetails = false` the three bodies become `{ "errors": [{ "parameterName", "message" }] }`, `{ "ruleName", "message" }` and `{ "error", "parameterName" }`; the two filters write `{ "<field>": ["<message>"] }` instead.

`OrionGuardProblemDetailsFactory.Create(...)` builds the same body from a `GuardResult`, an `AggregateValidationException` or a `BusinessRuleValidationException` if you want to answer with it yourself.

## Validate bound options at startup

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.AspNetCore.Extensions;
using Moongazing.OrionGuard.Attributes;

public sealed class SmtpSettings
{
    [NotNull, NotEmpty] public string Host { get; set; } = default!;
    [Range(1, 65535)] public int Port { get; set; }
}

public static class OptionsSetup
{
    public static void Add(IServiceCollection services) =>
        services.AddOptions<SmtpSettings>()
            .BindConfiguration("Smtp")
            .ValidateWithOrionGuardOnStart();
}
```

The options are checked against their OrionGuard attributes (`[NotNull]`, `[NotEmpty]`, `[Length]`, `[Email]`, `[Range]`, `[Regex]`, `[Positive]`) and, when one is registered, an `IValidator<TOptions>`. `ValidateWithOrionGuard()` is the same check without the startup pass, so it runs on first access instead.

## Health check

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.AspNetCore.Extensions;

public static class HealthSetup
{
    public static void Add(IServiceCollection services) =>
        services.AddHealthChecks().AddOrionGuardCheck(); // name "orionguard", tags "validation", "orionguard"
}
```

`Degraded` when no `IValidatorFactory` is registered, `Healthy` otherwise. A healthy result carries `ValidatorFactory` (the registered factory's type name) and `Version`, read from the `AssemblyInformationalVersionAttribute` of the OrionGuard core assembly actually loaded in the process.

## Options

| Option | Default | Effect |
| --- | --- | --- |
| `UseProblemDetails` | `true` | `false` makes the exception handler and both filters write plain JSON instead of `ProblemDetails` |
| `DefaultStatusCode` | `422` | Status for `AggregateValidationException`, and for filter failures whose validator suggests no status |
| `BusinessRuleStatusCode` | `422` | Status for `BusinessRuleValidationException`; set 400 for clients that expect it |
| `SuppressModelStateInvalidFilter` | `false` | `true` turns off MVC's automatic 400 for invalid model state, so OrionGuard writes every validation response |

## What this does not do

- **It does not find your validators.** There is no assembly scanning; a type with no registered `IValidator<T>` passes both filters silently. If an endpoint looks unvalidated, check the registration first.
- **The Minimal API filter runs one validator per type** — the last `IValidator<TRequest>` registered wins, because it resolves a single service. The MVC filter runs all of them. If you split rules across several validators for one type, the endpoint filter will only run one.
- **The Minimal API filter matches by argument type.** If the handler takes no argument of `TRequest`, or the argument is null, the handler runs unvalidated.
- **With `[ApiController]`, MVC's own model-state check runs first** and answers 400 for a body that could not bind, before OrionGuard sees anything. Set `SuppressModelStateInvalidFilter = true` if you want one consistent error shape.
- **The exception handler maps three exception types only.** Anything else is someone else's problem, by design.
- **Options validation is reflective**, so it is not trimming- or NativeAOT-safe. Validate options with a hand-written `IValidator<TOptions>` if you publish trimmed.
- **The health check does not validate anything.** It reports whether the validation stack is wired up, not whether any validator works.

## Targets

`net8.0`, `net9.0`, `net10.0`. Uses the ASP.NET Core shared framework; no package dependencies beyond OrionGuard itself.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) · [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR) (validate the command instead of the request) · [OrionGuard.Swagger](https://www.nuget.org/packages/OrionGuard.Swagger) (publish the same constraints in the schema) · [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
