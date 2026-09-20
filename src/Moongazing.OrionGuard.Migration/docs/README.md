# OrionGuard.Migration

A `dotnet tool` that rewrites FluentValidation validators onto OrionGuard — and reports, rather than guesses, every rule whose meaning would change.

```bash
dotnet new tool-manifest            # once per repository, if it has none
dotnet tool install OrionGuard.Migration
```

```bash
# Preview the changes without writing anything (the default).
dotnet orionguard migrate ./src --report
```

You get a unified diff per file and a report listing every construct the tool refused to translate, each already marked in place with a `// TODO: OrionGuard migration - ...` comment. Nothing is written until you ask:

```bash
dotnet orionguard migrate ./src --apply
```

A bare invocation never writes to disk: with neither `--report` nor `--apply`, `--report` wins. `--dry-run` is an alias for `--report`, and `--include` (default `*.cs`) narrows a directory scan — quote the glob so the shell leaves it alone:

```bash
dotnet orionguard migrate ./src/Validators/CreateUserValidator.cs --report
dotnet orionguard migrate ./src --apply --include "*Validator.cs"
```

`dotnet orionguard --help` prints the full usage. Installed globally with `-g`, the command is `orionguard` without the `dotnet` prefix. The migrated code compiles against the core `OrionGuard` package, so add it to every project you migrate:

```bash
dotnet add package OrionGuard
```

## What it rewrites

For each validator it recognises, the tool rewrites `using FluentValidation;` to `using Moongazing.OrionGuard.Compatibility;`, the base type `AbstractValidator<T>` to `FluentStyleValidator<T>`, and each fully supported `RuleFor(...)` chain to its OrionGuard equivalent.

A chain is rewritten **all or nothing**: if one rule in it has no safe equivalent, the whole chain is left alone and reported, because rewriting half a chain could quietly drop a rule.

A class counts as a FluentValidation validator only when it derives from `FluentValidation.AbstractValidator<T>` (fully qualified) or from a bare `AbstractValidator<T>` in a file that has `using FluentValidation;`. A bare `AbstractValidator<T>` without that using is left untouched, so another library's base class is never migrated — and for the same reason `RuleForEach(...)` and `Include(...)` are only reported when they appear inside a recognised validator and are called the FluentValidation way, so an EF Core `.Include(...)` is never mistaken for one.

These built-ins map across and give the same pass/fail result for every value; the test suite checks that by migrating, compiling and running one validator per rule:

| FluentValidation | OrionGuard |
| --- | --- |
| `NotNull()` | `NotNull()` |
| `NotEmpty()` | `NotEmpty()` |
| `Equal(value)` / `NotEqual(value)` | `Equal(value)` / `NotEqual(value)` |
| `Length(min, max)` | `Length(min, max)` |
| `Length(n)`, `n` a numeric literal | `Length(n, n)` |
| `MinimumLength(n)` / `MaximumLength(n)` | `MinimumLength(n)` / `MaximumLength(n)` |
| `GreaterThan` / `GreaterThanOrEqualTo` / `LessThan` / `LessThanOrEqualTo` | the same names |
| `InclusiveBetween(from, to)` / `ExclusiveBetween(from, to)` | the same names |
| `Must(value => ...)` | `Must(value => ...)` |
| `WithMessage("...")`, a string literal with no `{` | `WithMessage("...")` |
| `WithErrorCode(code)` | `WithErrorCode(code)` |
| `When(x => ...)` / `Unless(x => ...)` | the same names |

## Reported, not migrated

Recognised, left in place with a TODO and a report entry, because there is no safe one-to-one equivalent:

- **`Matches(...)`** — FluentValidation checks an empty or whitespace string against the pattern, so `""` fails `Matches("^[A-Z]+$")`. The compatibility builder skips blank values.
- **`EmailAddress()`** — FluentValidation only requires one `@` that is neither first nor last, and rejects `""`. The compatibility builder uses a stricter pattern, so `a@b` fails, and skips blank values.
- **`WithMessage(...)`** with anything but a string literal without `{`: a constant, a resource, an interpolated string, a literal containing `{PropertyName}`, or the `Func<T, string>` factory. FluentValidation fills placeholders in; the compatibility builder prints the text as written.
- **`ExactLength(...)`** — not a FluentValidation rule (the built-in is `Length(n)`), so it is treated as a custom extension.
- `Null()`, `Empty()`, `WithName(...)` / `OverridePropertyName(...)`, `Cascade(...)`, `ScalePrecision(...)` / `PrecisionScale(...)`, `MustAsync(...)`.
- `SetValidator(...)`, `InjectValidator(...)`, `RuleForEach(...)`, `Include(...)`, `ChildRules(...)`, `DependentRules(...)`, `Custom(...)`.
- Overloads whose argument shape is not translated: `EmailAddress(mode)`; the `Func<T, int>` overloads of `Length`; `Must`, `When` or `Unless` with a two- or three-parameter lambda; `Must(MethodName)` as a method group; and the member-comparison (lambda) overloads of `Equal`, `NotEqual` and the four comparisons.
- Any custom or unrecognised rule extension method.

## Exit codes

`0` — migration completed, nothing needs follow-up. `1` — it ran, but at least one construct needs manual work. `2` — usage error (bad arguments, or a path that does not exist). Wire `1` into CI if you want the follow-up list to stay empty.

## What this does not do

- **It does not preserve your messages.** Default error messages and error codes become OrionGuard's. If your clients read error text or codes, diff them before shipping.
- **One semantic difference survives on purpose:** a `double` or `float` `NaN` fails every comparison rule in OrionGuard. FluentValidation orders `NaN` below every number, so it lets `NaN` pass `LessThan` and `LessThanOrEqualTo`.
- **It rewrites C# source, nothing else.** Package references, DI registrations, `AddValidatorsFromAssembly` calls and anything that resolves `IValidator<T>` from FluentValidation are yours to update.
- **It does not compile or run your code.** The rewrite is syntactic; build and run your test suite afterwards. The all-or-nothing chain rule is what keeps a partial rewrite from compiling into a weaker validator.
- **It is not a FluentValidation emulator.** `FluentStyleValidator<T>` covers the rules above; a codebase leaning on child validators, cascade modes or custom validators will have a real follow-up list, and the report is honest about it rather than producing code that looks migrated.

## Requirements

The tool targets `net10.0`, so it needs the .NET 10 runtime to run — independently of what the projects it migrates target, which can be `net8.0`, `net9.0` or `net10.0`, as the core `OrionGuard` package supports.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) — `FluentStyleValidator<T>` lives in `Moongazing.OrionGuard.Compatibility` there.

## Documentation

- [OrionGuard README](https://github.com/tunahanaliozturk/OrionGuard#readme), including the FluentValidation migration section
- [CHANGELOG](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
