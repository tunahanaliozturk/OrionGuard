# OrionGuard

Guard clauses, object validation, and DDD building blocks for .NET. Guards throw on bad input,
validators collect every error into a `GuardResult`, and entity, aggregate and business-rule
primitives raise domain events. The core package targets `net8.0`, `net9.0` and `net10.0` and has one
dependency, `Microsoft.Extensions.DependencyInjection.Abstractions`.

The optional packages wire the same validators into ASP.NET Core, MediatR, MassTransit, Blazor,
gRPC, SignalR, Hangfire, EF Core and OpenTelemetry, or run them at compile time through a source
generator.

## Quick start (30 seconds)

```bash
dotnet add package OrionGuard
```

```csharp
using Moongazing.OrionGuard.Core;

public static class SignUp
{
    // Throws on the first failure. The parameter name ("email", "age", ...) comes from
    // CallerArgumentExpression, so nothing has to be repeated.
    public static void Check(string email, string password, int age)
    {
        Ensure.That(email).NotNull().NotEmpty().Email();
        Ensure.That(password).NotNull().MinLength(8);
        Ensure.That(age).InRange(18, 120);
    }

    // Or collect every failure and answer with it. GuardResult.ToErrorDictionary() has the shape
    // ASP.NET Core's ValidationProblem expects.
    public static Dictionary<string, string[]>? Collect(string email, string password)
    {
        GuardResult result = GuardResult.Combine(
            Ensure.Accumulate(email).NotNull().Email().ToResult(),
            Ensure.Accumulate(password).NotNull().MinLength(8).ToResult());

        return result.IsInvalid ? result.ToErrorDictionary() : null;
    }
}
```

That is the whole model: the same rules either throw or return a result, and every integration
package is a place where the result is produced and turned into a response.

## Where to go next

- [Getting started](docs/getting-started.md) — from the first guard to a validated ASP.NET Core endpoint.
- [Packages](packages/index.md) — one page per package, with the same text NuGet shows.
- [API reference](api/index.md) — generated from the XML documentation in the source.
- [Playground](playground/index.html) — edit JSON rules and try the guards in your browser; no install.
- [Migrating from FluentValidation](docs/migrating-from-fluentvalidation.md) — the codemod, and the differences it cannot rewrite.
- [Security guards](docs/security-guards.md) — what the injection heuristics do, and what actually defends you.
- [Outbox operations](docs/outbox-operations.md) — running, watching and repairing the transactional outbox.

## Project

- Source, issues and pull requests: [github.com/tunahanaliozturk/OrionGuard](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md) and [roadmap](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/ROADMAP.md)
- The next release is 7.0.0; [what is being built for it](docs/coming-in-7.md)
- MIT licensed
