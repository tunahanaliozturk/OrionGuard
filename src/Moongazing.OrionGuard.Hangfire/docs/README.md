# OrionGuard.Hangfire

Hangfire integration for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). It validates a background job's arguments at enqueue time, so the enqueue call rejects an invalid job and it never fails later inside a worker.

## Install

```bash
dotnet add package OrionGuard.Hangfire
```

The package depends on `Hangfire.Core`, and the core `OrionGuard` package is installed as a dependency. You choose the storage package yourself (`Hangfire.SqlServer`, `Hangfire.InMemory`, and so on).

## Quick start

Register your validators in DI, then wire the client filter to the same service provider:

```csharp
using Hangfire;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Hangfire;

builder.Services.AddOrionGuard();
builder.Services.AddValidator<SendEmailArgs, SendEmailArgsValidator>();

var app = builder.Build();

GlobalConfiguration.Configuration
    .UseInMemoryStorage() // from Hangfire.InMemory
    .UseOrionGuardValidation(app.Services);

// Equivalent, via the global filter collection:
// GlobalJobFilters.Filters.AddOrionGuardClientFilter(app.Services);
```

A validator is an ordinary OrionGuard validator for the argument type:

```csharp
public sealed record SendEmailArgs(string To, string Subject);

public sealed class SendEmailArgsValidator : AbstractValidator<SendEmailArgs>
{
    public SendEmailArgsValidator()
    {
        RuleFor(x => x.To, "To", p => p.NotEmpty().Email());
        RuleFor(x => x.Subject, "Subject", p => p.NotEmpty());
    }
}
```

The enqueue call itself now enforces the rules:

```csharp
// Passes validation: enqueued normally.
BackgroundJob.Enqueue<IEmailSender>(s => s.Send(new SendEmailArgs("user@example.com", "Hi")));

try
{
    // Fails validation: the job is never persisted and never reaches a worker.
    BackgroundJob.Enqueue<IEmailSender>(s => s.Send(new SendEmailArgs("not-an-email", "")));
}
catch (BackgroundJobClientException ex) when (ex.InnerException is JobArgumentValidationException invalid)
{
    foreach (var error in invalid.Errors)
        Console.WriteLine($"{error.ParameterName}: {error.Message}");
}
```

Hangfire's `BackgroundJobClient` wraps exceptions thrown during job creation in `BackgroundJobClientException`, so the `JobArgumentValidationException` arrives as its `InnerException`.

## What this package adds

- `OrionGuardClientFilter`: a Hangfire `IClientFilter`. In `OnCreating` it resolves `IValidator<T>` for the runtime type of each job argument and runs it. If any argument is invalid, it throws `JobArgumentValidationException`.
- `JobArgumentValidationException`: carries every `ValidationError` gathered across the job's arguments in `Errors`, plus `JobType` and `MethodName` for the target job method. Its `ToString()` appends one line per error.
- `UseOrionGuardValidation(IServiceProvider)` on `IGlobalConfiguration` and `AddOrionGuardClientFilter(IServiceProvider)` on `JobFilterCollection`: registration helpers for the two usual Hangfire setups.

Arguments that are `null`, or whose type has no registered validator, pass through unchanged.

## Validation details

- The filter opens a new DI scope for each job creation and resolves validators from that scope. Scoped validators, and validators with scoped dependencies, work and are disposed with the scope.
- It calls `ValidateAsync`, so both synchronous rules and `RuleForAsync` rules run. `OnCreating` is synchronous, so the filter blocks on that task during the enqueue call.
- The filter and both helpers are marked `[RequiresUnreferencedCode]` because validators are resolved by reflection over runtime argument types. If you trim or publish with NativeAOT, root your argument and validator types.

## Why enqueue-time

A job that is invalid by construction should fail where it was created, with a stack trace that points at the caller. It should not fail on a worker minutes later, where the only signal is a failed-job row. Validating in the client filter turns a bad enqueue into an immediate, structured exception at the call site.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- `Hangfire.Core` 1.8.25 or later 1.8.x
- `Newtonsoft.Json` 13.0.4 is referenced directly, which overrides the vulnerable 11.0.1 that `Hangfire.Core` would otherwise pull in

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard) (validators and `AddValidator`), [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry) (validation metrics and traces)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
