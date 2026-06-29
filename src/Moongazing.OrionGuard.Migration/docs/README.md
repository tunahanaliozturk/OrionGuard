# OrionGuard.Migration

A `dotnet tool` that migrates [FluentValidation](https://fluentvalidation.net/) validators to
OrionGuard. It reads your C# sources with Roslyn, finds classes deriving from
`AbstractValidator<T>`, and rewrites their `RuleFor(...)` chains onto the OrionGuard
`FluentStyleValidator<T>` compatibility surface shipped in the core `OrionGuard` package.

The tool is deliberately conservative. Anything it cannot translate safely is left exactly as it
was, marked with a `// TODO: OrionGuard migration - ...` comment, and listed in a final report. It
never guesses, so it never produces code that fails to compile or silently weakens validation.

## Install

```bash
dotnet tool install -g OrionGuard.Migration
```

## Use

```bash
# Preview the changes without writing anything (default).
dotnet orionguard migrate ./src --report

# Apply the changes in place.
dotnet orionguard migrate ./src --apply

# Migrate a single file.
dotnet orionguard migrate ./src/Validators/CreateUserValidator.cs --report

# Restrict the directory scan to a glob.
dotnet orionguard migrate ./src --apply --include *Validator.cs
```

If neither `--report` nor `--apply` is given the tool defaults to `--report`, so a bare invocation
never writes to disk.

## What it does

For every FluentValidation validator it finds, the tool:

1. Rewrites `using FluentValidation;` to `using Moongazing.OrionGuard.Compatibility;`.
2. Rewrites the base type `AbstractValidator<T>` to `FluentStyleValidator<T>`.
3. Rewrites each fully-supported `RuleFor(...)` chain to its OrionGuard equivalent.

A `RuleFor` chain is rewritten all-or-nothing: if any rule in the chain has no safe equivalent the
whole chain is left untouched and reported, because partially rewriting a chain could drop a rule.

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
- `SetValidator(...)`, `RuleForEach(...)`, `Include(...)`, `ChildRules(...)`, `DependentRules(...)`, `Custom(...)`
- Overloads whose argument shape is not translated (for example `EmailAddress(mode)`, the
  `WithMessage(Func<T, string>)` factory, or `Must` with a context argument)
- Any custom or unrecognised rule extension method

## Exit codes

- `0` migration completed; nothing needs manual follow-up.
- `1` migration ran but at least one construct needs manual follow-up.
- `2` usage error (bad arguments or a path that does not exist).
