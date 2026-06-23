# OrionGuard.Hangfire

Hangfire integration for [**OrionGuard**](https://github.com/tunahanaliozturk/OrionGuard). Validates a background job's arguments at enqueue time, so an invalid job is rejected by the enqueue call instead of failing later inside a worker.

## What this package adds

- **`OrionGuardClientFilter`**: a Hangfire `IClientFilter` whose `OnCreating` resolves the matching `IValidator<T>` for each job argument and runs it. If any argument is invalid it throws `JobArgumentValidationException`, which Hangfire surfaces to the caller that enqueued the job.
- **`JobArgumentValidationException`**: a structured exception carrying every `ValidationError` gathered across the job's arguments, plus the target job type and method name.
- **`UseOrionGuardValidation(...)` / `AddOrionGuardClientFilter(...)`**: registration helpers for both idiomatic Hangfire setups, the fluent `GlobalConfiguration` chain and the `GlobalJobFilters.Filters` collection.

Arguments that are `null`, or whose type has no registered validator, pass through untouched.

## Install

```bash
dotnet add package OrionGuard.Hangfire
```

Depends on `Hangfire.Core`; the core `OrionGuard` package is brought in transitively. The server packages (`Hangfire.SqlServer`, `Hangfire.InMemory`, ...) are yours to choose.

## Quick start

Register your validators in DI, then wire the client filter to the same service provider:

```csharp
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Hangfire;

builder.Services.AddOrionGuard();
builder.Services.AddValidator<SendEmailArgs, SendEmailArgsValidator>();

var app = builder.Build();

GlobalConfiguration.Configuration
    .UseInMemoryStorage()
    .UseOrionGuardValidation(app.Services);

// Equivalent, via the global filter collection:
// GlobalJobFilters.Filters.AddOrionGuardClientFilter(app.Services);
```

A validator is an ordinary OrionGuard validator for the argument type:

```csharp
public sealed class SendEmailArgsValidator : AbstractValidator<SendEmailArgs>
{
    public SendEmailArgsValidator()
    {
        RuleFor(x => x.To, "To", p => p.NotEmpty().Email());
        RuleFor(x => x.Subject, "Subject", p => p.NotEmpty());
    }
}
```

Now the enqueue itself enforces the rules:

```csharp
// Passes validation: enqueued normally.
BackgroundJob.Enqueue<IEmailSender>(s => s.Send(new SendEmailArgs("user@example.com", "Hi")));

// Fails validation: the Enqueue call throws JobArgumentValidationException.
// The job is never persisted and never reaches a worker.
BackgroundJob.Enqueue<IEmailSender>(s => s.Send(new SendEmailArgs("not-an-email", "")));
```

## Why enqueue-time

A job that is invalid by construction should fail where it was created, with a stack trace pointing at the caller, not on a worker minutes later where the only signal is a failed-job row. Validating in the client filter turns a bad enqueue into an immediate, structured exception at the call site.

## Targets

.NET 8.0, .NET 9.0, .NET 10.0. `Hangfire.Core` 1.8.x.

## License

MIT. See the [main repository](https://github.com/tunahanaliozturk/OrionGuard) for full docs, CHANGELOG, and samples.
