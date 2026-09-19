# OrionGuard.Migration

A `dotnet tool` that migrates [FluentValidation](https://fluentvalidation.net/) validators to
[OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). It reads your C# sources with Roslyn,
finds classes deriving from `AbstractValidator<T>`, and rewrites their `RuleFor(...)` chains onto
the OrionGuard `FluentStyleValidator<T>` compatibility surface shipped in the core `OrionGuard` package.

The tool is deliberately conservative. Anything it cannot translate safely is left exactly as it
was, marked with a `// TODO: OrionGuard migration - ...` comment, and listed in a final report. It
never guesses: the supported, verified rules are rewritten to compiling OrionGuard code, and
everything else is reported and left untouched rather than mistranslated.

## Install

As a local tool, invoked as `dotnet orionguard`:

```bash
dotnet new tool-manifest   # once per repository, if it has no tool manifest yet
dotnet tool install OrionGuard.Migration
```

Or as a global tool, invoked as `orionguard` (drop the `dotnet` prefix from the commands below):

```bash
dotnet tool install -g OrionGuard.Migration
```

The migrated validators compile against the core `OrionGuard` package, so add it to each project
you migrate:

```bash
dotnet add package OrionGuard
```

## Quick start

```bash
# Preview the changes without writing anything (default).
dotnet orionguard migrate ./src --report

# Apply the changes in place.
dotnet orionguard migrate ./src --apply

# Migrate a single file.
dotnet orionguard migrate ./src/Validators/CreateUserValidator.cs --report

# Restrict the directory scan to a glob (quote it so the shell does not expand it).
dotnet orionguard migrate ./src --apply --include "*Validator.cs"
```

If neither `--report` nor `--apply` is given the tool defaults to `--report`, so a bare invocation
never writes to disk. `--dry-run` is an alias for `--report`, and `--include` defaults to `*.cs`.
Run `dotnet orionguard --help` for the full usage.

## What it does

For every FluentValidation validator it finds, the tool:

1. Rewrites `using FluentValidation;` to `using Moongazing.OrionGuard.Compatibility;`.
2. Rewrites the base type `AbstractValidator<T>` to `FluentStyleValidator<T>`.
3. Rewrites each fully-supported `RuleFor(...)` chain to its OrionGuard equivalent.

A `RuleFor` chain is rewritten all-or-nothing: if any rule in the chain has no safe equivalent the
whole chain is left untouched and reported, because partially rewriting a chain could drop a rule.

A class is recognised as a FluentValidation validator only when it derives from
`FluentValidation.AbstractValidator<T>` (fully qualified) or from a bare `AbstractValidator<T>` in a
file that has a `using FluentValidation;` directive. A bare `AbstractValidator<T>` with no such using
is left untouched, so a type deriving from a different library's `AbstractValidator<T>` is never
migrated. For the same reason, `RuleForEach(...)` and `Include(...)` are only reported when they
appear inside a recognised validator and are invoked the FluentValidation way (as a bare call or on
`this`/`base`); an unrelated `.Include(...)` such as an EF Core query is left alone.

## Rules covered

These FluentValidation built-ins map directly onto the OrionGuard compatibility builder:

| FluentValidation | OrionGuard |
| ---------------- | ---------- |
| `NotNull()` | `NotNull()` |
| `NotEmpty()` | `NotEmpty()` |
| `Equal(x)` | `Equal(x)` |
| `NotEqual(x)` | `NotEqual(x)` |
| `Length(min, max)` | `Length(min, max)` |
| `MinimumLength(n)` | `MinimumLength(n)` |
| `MaximumLength(n)` | `MaximumLength(n)` |
| `ExactLength(n)` | `Length(n, n)` |
| `Matches(pattern)` | `Matches(pattern)` |
| `EmailAddress()` | `EmailAddress()` |
| `GreaterThan(x)` | `GreaterThan(x)` |
| `GreaterThanOrEqualTo(x)` | `GreaterThanOrEqualTo(x)` |
| `LessThan(x)` | `LessThan(x)` |
| `LessThanOrEqualTo(x)` | `LessThanOrEqualTo(x)` |
| `InclusiveBetween(a, b)` | `InclusiveBetween(a, b)` |
| `ExclusiveBetween(a, b)` | `ExclusiveBetween(a, b)` |
| `Must(predicate)` | `Must(predicate)` |
| `WithMessage("...")` | `WithMessage("...")` |
| `WithErrorCode("...")` | `WithErrorCode("...")` |
| `When(predicate)` | `When(predicate)` |
| `Unless(predicate)` | `Unless(predicate)` |

## Reported, not migrated

These are recognised but left untouched with a TODO and a report entry, because there is no safe
one-to-one equivalent on the compatibility builder:

- `Null()`, `Empty()`
- `WithName(...)` / `OverridePropertyName(...)`
- `Cascade(...)`
- `ScalePrecision(...)` / `PrecisionScale(...)`
- `MustAsync(...)`
- `SetValidator(...)`, `InjectValidator(...)`, `RuleForEach(...)`, `Include(...)`, `ChildRules(...)`,
  `DependentRules(...)`, `Custom(...)`
- Overloads whose argument shape is not translated (for example `EmailAddress(mode)`, the
  `WithMessage(Func<T, string>)` factory, `Must` with a context argument, or the member-comparison
  (lambda) overloads of `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqualTo`, `LessThan`, and
  `LessThanOrEqualTo`)
- Any custom or unrecognised rule extension method

## Exit codes

- `0` migration completed; nothing needs manual follow-up.
- `1` migration ran but at least one construct needs manual follow-up.
- `2` usage error (bad arguments or a path that does not exist).

## Requirements

- .NET 10 runtime to run the tool (it targets `net10.0`).
- The projects you migrate need the core `OrionGuard` package, which targets `net8.0`, `net9.0`,
  and `net10.0`.

## Documentation

- [OrionGuard README](https://github.com/tunahanaliozturk/OrionGuard#readme), including the
  FluentValidation migration section
- [CHANGELOG](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard) (provides
  `FluentStyleValidator<T>` in `Moongazing.OrionGuard.Compatibility`)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
