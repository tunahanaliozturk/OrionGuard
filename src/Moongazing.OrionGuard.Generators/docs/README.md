# OrionGuard.Generators

Roslyn incremental source generators for [**OrionGuard**](https://github.com/tunahanaliozturk/OrionGuard). Generates zero-reflection, NativeAOT-compatible validators and strongly-typed ID types from attributes.

## What this package generates

### `[GenerateValidator]` — compile-time validator

Add validation attributes to a DTO and the generator emits a static `Validate(...)` method that runs at compile-time speed without reflection.

```csharp
[GenerateValidator]
public sealed class CreateUserRequest
{
    [NotNull, NotEmpty, Length(3, 50)] public string Name { get; set; } = default!;
    [NotNull, Email]                   public string Email { get; set; } = default!;
    [Range(13, 120)]                   public int Age { get; set; }
}

var result = CreateUserRequestValidator.Validate(request);
```

### `[StronglyTypedId<TValue>]` — strongly-typed identifiers (**new in v6.1**)

Mark a `readonly partial struct` and the generator emits four companion sources:

- Partial struct body — `IEquatable<T>`, operators, `GetHashCode`, `ToString`, `Value` property, constructor, and (for `Guid`/`Ulid`) `New()` / `Empty`.
- **EF Core** `ValueConverter<TId, TValue>`.
- **System.Text.Json** `JsonConverter<TId>` with per-type `Read`/`Write`.
- **System.ComponentModel** `TypeConverter` (for ASP.NET Core route/query/form binding).

```csharp
[StronglyTypedId<Guid>]
public readonly partial struct OrderId;

[StronglyTypedId<int>]
public readonly partial struct SkuId;

[StronglyTypedId<string>]
public readonly partial struct CountryCode;
```

Supported underlying types: `System.Guid`, `int`, `long`, `string`, `System.Ulid` (net9.0+).

## Install

```bash
dotnet add package OrionGuard.Generators
```

This package is an analyzer/source-generator reference — it contributes generated code at compile time and does not add runtime dependencies to your output.

## Register EF Core converters

The generated EF Core `ValueConverter`s can be registered in DI with a single call from the core OrionGuard package:

```csharp
using Moongazing.OrionGuard.DependencyInjection;

services.AddOrionGuardStronglyTypedIds(); // scans the calling assembly
```

## Roslyn analyzers shipped

| Id | Description |
|----|-------------|
| `OG0001` | Missing validation attribute — emitted by `MissingValidationAnalyzer` when a parameter looks like it should be validated but isn't. |

## Targets

netstandard2.0 (Roslyn-compatible). Consumes any `net8.0+` project.

## License

MIT. See the [main repository](https://github.com/tunahanaliozturk/OrionGuard) for full docs, CHANGELOG, and samples.
