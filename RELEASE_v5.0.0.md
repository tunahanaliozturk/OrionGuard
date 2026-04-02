# OrionGuard v5.0.0 — Release Notes

**Release Date:** April 2, 2026
**Target Frameworks:** .NET 8.0, .NET 9.0
**NuGet Package:** [OrionGuard](https://www.nuget.org/packages/OrionGuard)
**Repository:** [github.com/Moongazing/OrionGuard](https://github.com/Moongazing/OrionGuard)

---

## Overview

OrionGuard v5.0.0 is a major release focused on **performance**, **security**, and **global format validation**. This version introduces the ThrowHelper pattern for JIT-friendly guard methods, a brand-new Security Guards module to defend against injection attacks, universal Format Guards replacing country-specific validators, span-based FastGuard extensions, thread-safe localization across 8 languages, and a comprehensive test suite.

---

## What's New

### ThrowHelper Pattern

All hot-path guard methods now delegate exception throwing to a centralized `ThrowHelper` static class. Each method is annotated with `[DoesNotReturn]`, `[StackTraceHidden]`, and `[MethodImpl(MethodImplOptions.NoInlining)]`, which:

- Keeps validated method bodies small so the JIT can inline them aggressively.
- Produces cleaner stack traces by hiding framework-internal frames.
- Centralizes exception construction for consistency.

```csharp
// Before (v4) — throw inline bloats the method body
public static void NotNull<T>(T value, string parameterName)
{
    if (value is null)
        throw new NullValueException(parameterName);
}

// After (v5) — ThrowHelper keeps the happy path lean
public static void NotNull<T>(T value, string parameterName)
{
    if (value is null)
        ThrowHelper.ThrowNullValue(parameterName);
}
```

### Security Guards (New Module)

A dedicated `SecurityGuards` extension class provides protection against the most common web application attack vectors. All pattern sets are stored in `FrozenSet<string>` for O(1) lookup performance.

| Guard Method | Description |
|---|---|
| `AgainstSqlInjection` | Detects 28 SQL injection patterns (keywords, operators, system objects) |
| `AgainstXss` | Detects 28 XSS vectors including script tags, event handlers, and DOM sinks |
| `AgainstPathTraversal` | Catches `../`, encoded variants, and known sensitive paths |
| `AgainstCommandInjection` | Blocks shell metacharacters, pipe operators, and interpreter commands |
| `AgainstLdapInjection` | Span-based detection of LDAP-special characters |
| `AgainstXxe` | Detects DOCTYPE and ENTITY declarations indicative of XXE attacks |
| `AgainstInjection` | Combined check — runs SQL, XSS, path traversal, and command injection in one call |
| `AgainstUnsafeFileName` | Validates filenames against traversal sequences and invalid OS characters |
| `AgainstOpenRedirect` | Validates redirect URLs against an allow-list of trusted domains |

```csharp
userInput.AgainstSqlInjection(nameof(userInput));
userInput.AgainstXss(nameof(userInput));
filePath.AgainstPathTraversal(nameof(filePath));
command.AgainstCommandInjection(nameof(command));

// Or run all common checks at once:
userInput.AgainstInjection(nameof(userInput));
```

### Format Guards (Replaces TurkishGuards)

The country-specific `TurkishGuards` module has been removed in favor of a universal `FormatGuards` class that covers internationally applicable format validations.

| Guard Method | Description |
|---|---|
| `AgainstInvalidLatitude` / `AgainstInvalidLongitude` | Geographic coordinate range validation |
| `AgainstInvalidCoordinates` | Combined lat/lng validation |
| `AgainstInvalidMacAddress` | MAC address validation (colon, hyphen, or dot notation) |
| `AgainstInvalidHostname` | RFC 1123 hostname validation with label length rules |
| `AgainstInvalidCidr` | CIDR notation validation for IPv4 and IPv6 |
| `AgainstInvalidCountryCode` | ISO 3166-1 alpha-2 validation (249 country codes) |
| `AgainstInvalidTimeZoneId` | Validates against IANA/Windows time zone database |
| `AgainstInvalidLanguageTag` | BCP 47 / IETF language tag validation |
| `AgainstInvalidJwtFormat` | Structural JWT validation (three Base64URL segments) |
| `AgainstInvalidConnectionString` | Key=value connection string format validation |
| `AgainstInvalidBase64String` | Base64 encoding structure and padding validation |

```csharp
latitude.AgainstInvalidLatitude(nameof(latitude));
"US".AgainstInvalidCountryCode(nameof(countryCode));
"Europe/Istanbul".AgainstInvalidTimeZoneId(nameof(timeZone));
token.AgainstInvalidJwtFormat(nameof(token));
```

### Span-Based FastGuard Extensions

New zero-allocation validation methods using `ReadOnlySpan<char>`:

| Method | Description |
|---|---|
| `FastGuard.Email(string, string)` | Email format validation via span parsing — no regex |
| `FastGuard.Ascii(ReadOnlySpan<char>, string)` | Ensures all characters are in the ASCII range |
| `FastGuard.AlphaNumeric(ReadOnlySpan<char>, string)` | Rejects non-alphanumeric characters |
| `FastGuard.NumericString(ReadOnlySpan<char>, string)` | Digits-only span validation |
| `FastGuard.MaxLength(ReadOnlySpan<char>, int, string)` | Maximum length check |
| `FastGuard.ValidGuid(ReadOnlySpan<char>, string)` | GUID format and non-empty check |
| `FastGuard.Finite(double, string)` | Rejects `NaN` and `Infinity` |

```csharp
FastGuard.Email(email, nameof(email));
FastGuard.Ascii(input.AsSpan(), nameof(input));
FastGuard.AlphaNumeric(code.AsSpan(), nameof(code));
FastGuard.Finite(price, nameof(price));
```

### Thread-Safe Localization (8 Languages)

`ValidationMessages` has been rewritten with `ConcurrentDictionary` and `AsyncLocal<CultureInfo>` for safe per-request culture scoping in multi-threaded server environments.

**Supported Languages:** English, Turkish, German, French, Spanish, Portuguese, Arabic, Japanese

Each language includes 30+ message keys with automatic English fallback for missing entries.

```csharp
// Set culture for the current async scope (does not affect other threads)
ValidationMessages.SetCultureForCurrentScope(new CultureInfo("de"));

// Messages are now returned in German
var message = ValidationMessages.Get("NotNull", "Email");
// → "Email darf nicht null sein."
```

### ObjectValidator Enhancements

- **Compiled Expression Caching:** Property accessors are compiled once via `Expression.Lambda` and stored in a `ConcurrentDictionary`, eliminating repeated reflection overhead.
- **Cross-Property Validation:** `CrossProperty<TProp1, TProp2>()` allows validation rules that span two properties.
- **Conditional Validation:** `When(bool, Action<ObjectValidator<T>>)` enables blocks that only execute when a condition is met.

```csharp
var result = Validate.For(order)
    .Property(o => o.StartDate, g => g.NotNull())
    .Property(o => o.EndDate, g => g.NotNull())
    .CrossProperty<DateTime, DateTime>(
        o => o.StartDate, o => o.EndDate,
        (start, end) => start < end,
        "StartDate must be before EndDate")
    .When(order.IsExpress, v =>
        v.Property(o => o.Priority, g => g.InRange(1, 3)))
    .ToResult();
```

### FluentGuard Enhancements

- **`Transform(Func<T, T>)`** — Apply in-pipeline transformations (trim, lowercase, etc.) during validation.
- **`Default(T)`** — Replace null values with a specified default before subsequent checks.
- All date comparisons now use `DateTime.UtcNow` instead of `DateTime.Now`.

```csharp
var email = Ensure.That(rawEmail)
    .Transform(e => e.Trim().ToLowerInvariant())
    .NotEmpty()
    .Email()
    .Value;
```

### Sealed Exceptions with Structured Data

All custom exception classes are now `sealed` and include two new properties:

- **`ErrorCode`** — A machine-readable string (e.g., `"NULL_VALUE"`, `"INVALID_EMAIL"`) for programmatic error handling.
- **`ParameterName`** — The name of the parameter that failed validation.

```csharp
try
{
    Guard.AgainstNull(value, nameof(value));
}
catch (GuardException ex)
{
    logger.LogWarning("Validation failed: {ErrorCode} on {Param}", ex.ErrorCode, ex.ParameterName);
}
```

### CI/CD Pipeline

A new GitHub Actions workflow (`ci-cd.yml`) replaces the previous `dotnet-desktop.yml`:

- **Build & Test:** Runs on every push and PR against `main`/`master`, testing on both .NET 8.0 and .NET 9.0.
- **Publish:** On GitHub Release, automatically packs and publishes to both **NuGet.org** and **GitHub Packages**.

### Comprehensive Test Suite

Six new test classes added, covering all new modules:

| Test Class | Coverage Area |
|---|---|
| `FastGuardTests` | All span-based FastGuard methods |
| `FluentGuardTests` | Transform, Default, date comparisons, chaining |
| `FormatGuardsTests` | All format validation guards |
| `ObjectValidatorTests` | Cross-property, conditional, compiled caching |
| `SecurityGuardsTests` | SQL injection, XSS, path traversal, command injection, XXE, LDAP |
| `ValidationMessagesTests` | All 8 languages, culture scoping, fallback behavior |

---

## Breaking Changes

| Change | Migration |
|---|---|
| All exceptions are now `sealed` | Remove any subclasses of OrionGuard exceptions |
| `Validate.Object<T>()` → `Validate.For<T>()` | Find & replace |
| `Validate.ObjectStrict<T>()` → `Validate.ForStrict<T>()` | Find & replace |
| `FastGuard.Guid()` → `FastGuard.ValidGuid()` | Find & replace |
| `TurkishGuards` removed | Use `FormatGuards` equivalents |
| `AgainstEmptyCollection` throws `NullValueException` | Update catch blocks if needed |
| `AgainstNotAllLowercase` behavior corrected | Previously always passed — review usages |

---

## Bug Fixes

- **`AgainstNotAllLowercase`** was comparing the string to itself with `InvariantCultureIgnoreCase`, which always returned `true`. Now correctly compares against `ToLowerInvariant()` with `Ordinal` comparison.
- **`AgainstEmptyCollection`** was throwing `EmptyStringException` for empty collections. Now correctly throws `NullValueException`.
- **`GuardBuilderExtensions`** was passing `.Value` instead of `.ParameterName` in error messages.
- **Missing `using System.Diagnostics`** in ThrowHelper and FastGuard resolved `StackTraceHidden` build errors.
- Multiple CA analyzer violations resolved: CA1305, CA1310, CA1720, CA1845, CA1862, CA2263.

---

## Performance Improvements

| Optimization | Impact |
|---|---|
| ThrowHelper pattern | Smaller JIT-compiled method bodies → better inlining |
| `FrozenSet<string>` for pattern matching | O(1) lookups for security and business guard patterns |
| Compiled expression caching | Eliminates repeated `Expression.Compile()` in ObjectValidator |
| `RegexCache` with bounded size (1000) | Replaces all raw `Regex.IsMatch` calls |
| `ReadOnlySpan<char>` FastGuard overloads | Zero-allocation validation on hot paths |
| `ICollection<T>.Count` check before enumeration | Avoids unnecessary LINQ allocation in `AgainstExceedingCount` |
| `StringComparison` parameters on all string ops | Avoids culture-sensitive overhead in validation logic |

---

## Installation

```bash
dotnet add package OrionGuard --version 5.0.0
```

Or via the NuGet Package Manager:

```
Install-Package OrionGuard -Version 5.0.0
```

---

## Quick Start

```csharp
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Extensions;

// Basic guard
Guard.AgainstNull(user, nameof(user));

// Fluent validation
var email = Ensure.That(rawEmail)
    .Transform(e => e.Trim().ToLowerInvariant())
    .NotNull()
    .NotEmpty()
    .Email()
    .Value;

// Security checks
userInput.AgainstInjection(nameof(userInput));

// Format validation
coordinates.AgainstInvalidLatitude(nameof(latitude));
"US".AgainstInvalidCountryCode(nameof(country));

// High-performance path
FastGuard.NotNullOrEmpty(name, nameof(name));
FastGuard.Email(email, nameof(email));
FastGuard.Ascii(code.AsSpan(), nameof(code));
```

---

## Full Changelog

See [CHANGELOG.md](CHANGELOG.md) for the complete list of changes.

## Author

**Tunahan Ali Ozturk** — Creator & Maintainer

## License

MIT License — see [LICENSE.txt](src/Moongazing.OrionGuard/docs/LICENSE.txt)
