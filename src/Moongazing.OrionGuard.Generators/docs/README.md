# OrionGuard.Generators

Roslyn source generator and analyzer for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). Mark a type with `[GenerateValidator]`, put OrionGuard validation attributes on its properties, and the generator writes a reflection-free static validator for it at compile time.

> **Deprecated: `[StronglyTypedId<TValue>]`.** The strongly-typed id generator in this package is deprecated since v6.4.0 in favour of the standalone [OrionKey](https://github.com/tunahanaliozturk/OrionKey) package (`[OrionId<TValue>]`). It still works through the v6.x line and is removed in v7.0.0. Follow the [migration guide](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/stronglytypedid-to-orionkey.md). Only the generator is deprecated: the manual `StronglyTypedId<TValue>` record in the core `OrionGuard` package is not.

## Install

```bash
dotnet add package OrionGuard
dotnet add package OrionGuard.Generators
```

The generated code uses types from the core `OrionGuard` package (the validation attributes and `GuardResult`), so reference both. `OrionGuard.Generators` is a development dependency: it runs inside the compiler and adds nothing to your build output. It needs .NET SDK 10.0.400 or newer (see Requirements below).

## Quick start

```csharp
using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Generators;

namespace MyApp;

[GenerateValidator]
public sealed class CreateUserRequest
{
    [NotNull, NotEmpty, Length(3, 50)] public string Name { get; set; } = default!;
    [NotNull, Email]                   public string Email { get; set; } = default!;
    [Range(13, 120)]                   public int Age { get; set; }
}
```

The generator emits `CreateUserRequestValidator` in the same namespace:

```csharp
using Moongazing.OrionGuard.Core;

namespace MyApp;

public static class Signup
{
    public static Dictionary<string, string[]>? Check(CreateUserRequest request)
    {
        GuardResult result = CreateUserRequestValidator.Validate(request);
        return result.IsInvalid ? result.ToErrorDictionary() : null;
    }

    // Returns the request, or throws AggregateValidationException with every error.
    public static CreateUserRequest Accept(CreateUserRequest request) =>
        CreateUserRequestValidator.ValidateAndThrow(request);
}
```

## What `[GenerateValidator]` generates

For each class or struct marked `[GenerateValidator]`, the generator emits `public static class <TypeName>Validator` with two methods:

- `GuardResult Validate(<TypeName> instance)` collects every error.
- `<TypeName> ValidateAndThrow(<TypeName> instance)` returns the instance, or throws `AggregateValidationException` when validation fails.

These attributes from `Moongazing.OrionGuard.Attributes` are translated into generated checks:

| Attribute | Generated check | Default error code |
| --- | --- | --- |
| `[NotNull]` | value is not null | `NOT_NULL` |
| `[NotEmpty]` | string is not null or whitespace; collection is not null or empty | `NOT_EMPTY` |
| `[Length(min, max)]` | string length is within `min`..`max` | `LENGTH` |
| `[Email]` | value looks like an email address | `EMAIL` |
| `[Range(min, max)]` | numeric value is within `min`..`max` | `RANGE` |
| `[Positive]` | numeric value is greater than zero | `POSITIVE` |
| `[Regex(pattern)]` | value matches the pattern | `REGEX` |

Set the `ErrorMessage` and `ErrorCode` named arguments on any of these attributes to override the default message and code, for example `[Email(ErrorMessage = "Enter a valid email.", ErrorCode = "BAD_EMAIL")]`.

Notes:

- The null and empty checks run first; the other checks for a property run only when those pass, so a null value reports one error, not several.
- Only public properties with at least one of these attributes are validated. A type with no such property gets no generated validator.
- The `[GenerateValidator]` attribute itself is injected into your compilation by the generator, in the `Moongazing.OrionGuard.Generators` namespace.

## Analyzer diagnostics

| ID | Category | Severity | Meaning |
| --- | --- | --- | --- |
| `OG0001` | Usage | Warning | A public property on a `[GenerateValidator]` type has no attribute from `Moongazing.OrionGuard.Attributes`, so the generated validator accepts any value for it. |

Types without `[GenerateValidator]` are never analyzed.

## Deprecated: `[StronglyTypedId<TValue>]`

Still shipped in v6.x for existing code. On a `readonly partial struct`, `[StronglyTypedId<TValue>]` (namespace `Moongazing.OrionGuard.Domain.Primitives`) generates the struct body (`Value`, constructor, equality, `IStronglyTypedId<TValue>`), a `System.Text.Json` converter, a `TypeConverter`, `IParsable` / `ISpanParsable`, and an EF Core `ValueConverter` when the project references EF Core. Supported value types are `Guid`, `int`, `long`, and `string`. The core package's `services.AddOrionGuardStronglyTypedIds()` registers the generated EF Core converters.

The attribute is marked `[Obsolete]`, so every use produces compiler warning CS0618. To move to OrionKey:

```bash
dotnet add package OrionKey
```

| OrionGuard.Generators | OrionKey |
| --- | --- |
| `using Moongazing.OrionGuard.Domain.Primitives;` | `using Moongazing.OrionKey;` |
| `[StronglyTypedId<Guid>]` | `[OrionId<Guid>]` |
| `[StronglyTypedId<int>]` | `[OrionId<int>]` |

The [migration guide](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/stronglytypedid-to-orionkey.md) covers `long` and `string` ids and the other differences.

## Requirements

- **.NET SDK 10.0.400 or newer.** The generators are built against `Microsoft.CodeAnalysis.CSharp` 5.9.0 (Roslyn 5.9). Older SDKs, including every .NET 8 and .NET 9 SDK, report warning CS9057 and skip the generators, so the `<TypeName>Validator` classes are not generated and code that calls them does not compile.
- The package targets `netstandard2.0`, as Roslyn components must. Your project can target `net8.0`, `net9.0`, or `net10.0` (the targets of the core `OrionGuard` package) as long as it builds with SDK 10.0.400 or newer.

## Documentation

- [OrionGuard README](https://github.com/tunahanaliozturk/OrionGuard#readme)
- [CHANGELOG](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- [`[StronglyTypedId]` to OrionKey migration guide](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/stronglytypedid-to-orionkey.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard) (required), [OrionGuard.OpenApi](https://www.nuget.org/packages/OrionGuard.OpenApi) (generates validators from an OpenAPI schema), [OrionKey](https://github.com/tunahanaliozturk/OrionKey) (strongly-typed ids)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
