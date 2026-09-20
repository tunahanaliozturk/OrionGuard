# OrionGuard.Hangfire

Rejects an invalid background job at the `Enqueue` call instead of letting it fail on a worker minutes later, with nothing but a failed-job row to explain it.

```bash
dotnet add package OrionGuard.Hangfire
```

```csharp
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Hangfire;

public sealed record SendEmailArgs(string To, string Subject);

public sealed class SendEmailArgsValidator : AbstractValidator<SendEmailArgs>
{
    public SendEmailArgsValidator()
    {
        RuleFor(x => x.To, "To", p => p.NotEmpty().Email());
        RuleFor(x => x.Subject, "Subject", p => p.NotEmpty());
    }
}

public static class HangfireSetup
{
    public static void Add(IServiceCollection services) =>
        services
            .AddOrionGuard()
            .AddValidator<SendEmailArgs, SendEmailArgsValidator>();

    // Call once the provider exists, after .UseXxxStorage(...) on the same chain.
    public static void Configure(IServiceProvider provider) =>
        GlobalConfiguration.Configuration.UseOrionGuardValidation(provider);
}
```

From then on the enqueue call enforces the rules:

```csharp
using Hangfire;
using Moongazing.OrionGuard.Hangfire;

public interface IEmailSender
{
    void Send(SendEmailArgs args);
}

public sealed record SendEmailArgs(string To, string Subject);

public static class EnqueueEmail
{
    public static void Run()
    {
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
            {
                Console.WriteLine($"{error.ParameterName}: {error.Message}");
            }
        }
    }
}
```

`JobArgumentValidationException` carries every `ValidationError` gathered across the job's arguments in `Errors`, plus `JobType` and `MethodName`; its `ToString()` appends one line per error. Hangfire wraps anything thrown during job creation in `BackgroundJobClientException`, which is why the real exception arrives as `InnerException`.

The core `OrionGuard` package comes along as a dependency; the storage package (`Hangfire.SqlServer`, `Hangfire.InMemory`, ...) is your choice.

## Two ways to register the filter

`UseOrionGuardValidation(IServiceProvider)` on `IGlobalConfiguration`, as above, or `GlobalJobFilters.Filters.AddOrionGuardClientFilter(provider)` on a `JobFilterCollection` if that is how your app is set up. Both install the same `OrionGuardClientFilter`.

## How the check runs

`OrionGuardClientFilter` is an `IClientFilter`. In `OnCreating` it opens a DI scope of its own for the job creation, then for each non-null argument runs every `IValidator<T>` registered for that argument's runtime type, one after another, through `ValidateAsync` — so `RuleForAsync` rules run too. The scope, and anything scoped it resolved, is disposed afterwards.

## What this does not do

- **Enqueue-time only.** A job that was already persisted — by an older build, by another service, or before you added a rule — is not re-checked when a worker picks it up.
- **`OnCreating` is synchronous**, so the filter blocks the enqueue call while async rules run. Keep `RuleForAsync` work off the enqueue path if enqueueing is on a request thread.
- **It does not find your validators.** No assembly scanning; register each with `AddValidator<T, TValidator>()`. An argument type with no validator, and a `null` argument, pass through.
- **Matching is by the argument's runtime type**, so a validator registered for a widely-used type (`string`, `Guid`) applies to every job argument of that type in the process.
- **Not trimming- or NativeAOT-safe.** The filter and both registration helpers are marked `[RequiresUnreferencedCode]`, because validators are resolved reflectively from runtime argument types. Root your argument and validator types if you publish trimmed.
- **It checks the arguments, not the world.** "This customer still exists" is true at enqueue time and may be false when the worker runs; that check belongs in the job.

## Targets

`net8.0`, `net9.0`, `net10.0`; `Hangfire.Core` 1.8.x. `Newtonsoft.Json` is referenced directly at a patched version, so the vulnerable one `Hangfire.Core` would otherwise pull in transitively is not what you get.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) (validators and `AddValidator`) · [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR) · [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
