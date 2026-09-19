# Migrating from FluentValidation

OrionGuard ships a compatibility surface, `FluentStyleValidator<T>` in
`Moongazing.OrionGuard.Compatibility`, that keeps the `RuleFor(x => x.Email).NotEmpty().EmailAddress()`
shape. A migrated validator is usually the same file with two lines changed: the `using` and the base
class.

There is also a codemod, the `OrionGuard.Migration` dotnet tool, that makes those changes for you
across a repository. It is deliberately conservative: a rule it cannot translate safely is left
exactly as it was, marked with a `// TODO: OrionGuard migration - ...` comment, and listed in a
report.

Read this page in order: run the tool, then work through what it reported, then check the
behaviour differences at the end — those are the ones no codemod can fix for you.

## Run the codemod

```bash
dotnet new tool-manifest              # once per repository, if it has none
dotnet tool install OrionGuard.Migration
dotnet add package OrionGuard         # in every project you migrate
```

```bash
# Print the diff and the report; writes nothing. This is also what a bare invocation does.
dotnet orionguard migrate ./src --report

# Write the changes in place.
dotnet orionguard migrate ./src --apply

# One file, or a narrower scan (quote the glob so the shell does not expand it).
dotnet orionguard migrate ./src/Validators/CreateUserValidator.cs --report
dotnet orionguard migrate ./src --apply --include "*Validator.cs"
```

`--dry-run` is an alias for `--report`, and `--include` defaults to `*.cs`. The run ends with a
summary:

```text
OrionGuard migration summary
============================
Mode:           report
Files scanned:  42
Files to change: 17
Manual follow-ups: 3

The following constructs were left untouched (see TODO markers):
  src/Validators/OrderValidator.cs:24  MustAsync - no equivalent on the compatibility builder
```

Exit codes: `0` nothing needs follow-up, `1` at least one construct does, `2` a usage error (bad
arguments, or a path that does not exist). In CI, treat `1` as "read the report", not as a crash.

A class is only recognised as a FluentValidation validator when it derives from
`FluentValidation.AbstractValidator<T>`, or from a bare `AbstractValidator<T>` in a file that has
`using FluentValidation;`. A different library's `AbstractValidator<T>` is left alone.

## What changes in the file

Before:

```csharp
using FluentValidation;

public sealed record Customer(string Name, string Email, int Age);

public sealed class CustomerValidator : AbstractValidator<Customer>
{
    public CustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithErrorCode("EMAIL");
        RuleFor(x => x.Age).InclusiveBetween(18, 120).WithMessage("Adults only.");
    }
}
```

After:

```csharp
using Moongazing.OrionGuard.Compatibility;

public sealed record Customer(string Name, string Email, int Age);

public sealed class CustomerValidator : FluentStyleValidator<Customer>
{
    public CustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithErrorCode("EMAIL");
        RuleFor(x => x.Age).InclusiveBetween(18, 120).WithMessage("Adults only.");
    }
}
```

The constructor body is untouched, so validators that take dependencies keep working: the class is
an ordinary type, resolved from DI like any other.

## Rules the codemod rewrites

| FluentValidation | OrionGuard |
| --- | --- |
| `NotNull()`, `NotEmpty()` | the same |
| `Equal(x)`, `NotEqual(x)` | the same |
| `Length(min, max)`, `MinimumLength(n)`, `MaximumLength(n)` | the same |
| `ExactLength(n)` | `Length(n, n)` |
| `Matches(pattern)`, `EmailAddress()` | the same |
| `GreaterThan`, `GreaterThanOrEqualTo`, `LessThan`, `LessThanOrEqualTo` | the same |
| `InclusiveBetween(a, b)`, `ExclusiveBetween(a, b)` | the same |
| `Must(predicate)` | the same |
| `WithMessage("...")`, `WithErrorCode("...")` | the same |
| `When(predicate)`, `Unless(predicate)` | the same |

A `RuleFor` chain is rewritten all or nothing. If one rule in the chain has no safe equivalent, the
whole chain is left as it was, because rewriting half a chain could drop a rule.

## What it reports, and what to do about it

| Reported construct | What to do |
| --- | --- |
| `MustAsync(...)` | The compatibility builder has no async rule. Derive the validator from `AbstractValidator<T>` instead and use `RuleForAsync`, as below, or move the check into `Validate.For(x).MustAsync(...)`. |
| `SetValidator(...)`, `InjectValidator(...)`, `RuleForEach(...)`, `ChildRules(...)` | Child and collection validation lives in `Validate.Nested(obj)`. Call it from the parent validator, or validate the child object where it is created. |
| `Null()`, `Empty()` | Express as a predicate: `RuleFor(x => x.MiddleName).Must(v => v is null).WithMessage("...")`. |
| `WithName(...)`, `OverridePropertyName(...)` | The error's `ParameterName` comes from the property expression and cannot be renamed on the builder. Rename the property, or produce the error yourself with `AbstractValidator.RuleFor(predicate, message, propertyName)`. |
| `Cascade(...)` | There is no cascade mode: every rule added to a builder runs, and each failing rule adds its own error. Where FluentValidation stopped after the first failure, expect more errors for the same property. |
| `ScalePrecision(...)` / `PrecisionScale(...)` | Write it as a `Must(...)` predicate over the decimal. |
| `DependentRules(...)`, `Custom(...)` | Rewrite as ordinary rules, or as `AbstractValidator.RuleFor(predicate, message, propertyName)` which gives you the whole instance. |
| Overloads with a different argument shape — `EmailAddress(mode)`, `WithMessage(Func<T, string>)`, `Must` with a context, the member-comparison (lambda) overloads of `Equal`, `GreaterThan`, ... | Pick the plain overload where the semantics allow it, otherwise write the rule as a predicate. |
| Any custom rule extension method of your own | Port the extension to `FluentRuleBuilder<T, TProperty>`, or inline its predicate. |

An async rule after migration:

```csharp
using Moongazing.OrionGuard.DependencyInjection;

public sealed record Registration(string Email);

public interface IUserDirectory
{
    Task<bool> IsTakenAsync(string email);
}

public sealed class RegistrationValidator : AbstractValidator<Registration>
{
    public RegistrationValidator(IUserDirectory directory)
    {
        RuleFor(x => x.Email, nameof(Registration.Email), p => p.NotEmpty().Email());

        RuleForAsync(
            async registration => !await directory.IsTakenAsync(registration.Email),
            "Email is already registered.",
            nameof(Registration.Email))
            .WithErrorCode("EMAIL_TAKEN");
    }
}
```

`AbstractValidator<T>` and `FluentStyleValidator<T>` both implement `IValidator<T>`, so the rest of
your application does not care which one a validator derives from.

## Behaviour differences to check after the migration

| | FluentValidation | OrionGuard |
| --- | --- | --- |
| Result type | `ValidationResult` with `IsValid` and `Errors` | `GuardResult` with `IsValid`, `IsInvalid`, `Errors`, `Warnings`, `Infos` |
| One error | `ValidationFailure.PropertyName` / `.ErrorMessage` / `.ErrorCode` | `ValidationError.ParameterName` / `.Message` / `.ErrorCode` / `.Severity` |
| Throwing | `ValidateAndThrow` throws `ValidationException` | `validator.Validate(x).ThrowIfInvalid()` throws `AggregateValidationException` |
| Async | `ValidateAsync` runs async rules | `FluentStyleValidator<T>.ValidateAsync` runs the same sync rules and returns a completed task; async rules need `AbstractValidator<T>` or `Validate.For` |
| Cascade | configurable, per validator or per rule | not configurable: every rule on a builder runs |
| Conditions | `When` applies to the rules it is chained after | the same: `When` wraps every rule defined so far on that builder, and `Unless` is its negation |
| Error messages | FluentValidation's text and placeholders | OrionGuard's text, in 14 languages, for example `'Email' must not be empty.` Assertions on message strings need rewriting. |
| Registration | `AddValidatorsFromAssembly`, auto-validation | `services.AddOrionGuard()` plus one `AddValidator<T, TValidator>()` per validator; nothing scans assemblies |
| ASP.NET Core | model-level auto-validation | an endpoint filter (`.WithValidation<T>()`) or `[ValidateRequest]` on MVC actions, answering with RFC 9457 ProblemDetails |

Registration after the migration:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

public static class ValidatorRegistration
{
    public static IServiceCollection AddValidators(this IServiceCollection services) =>
        services
            .AddOrionGuard()
            .AddValidator<Customer, CustomerValidator>();
}
```

If a validator has many registrations to add, `AddOrionGuard(registry => registry.Register<...>())`
takes them in one call.

## Suggested order of work

1. Commit a clean tree, then run `dotnet orionguard migrate ./src --report` and read the summary.
2. Run it again with `--apply`, and review the diff.
3. Build. Anything that still references `FluentValidation` is either a reported construct or a
   custom extension; work through the TODO markers.
4. Replace `AddValidatorsFromAssembly` and auto-validation with explicit registrations and the
   ASP.NET Core filter.
5. Run your tests. Expect assertions on message text and on the number of errors per property to
   need updating.
6. Remove the `FluentValidation` package reference.

## See also

- [OrionGuard.Migration](../packages/migration.md) — the tool's own README, including every rule it
  recognises.
- [Moongazing.OrionGuard.Compatibility](xref:Moongazing.OrionGuard.Compatibility) — `FluentStyleValidator<T>`
  and `FluentRuleBuilder<T, TProperty>` in the API reference.
- [Getting started](getting-started.md) — `Validate.For`, `AbstractValidator<T>` and the ASP.NET Core wiring.
