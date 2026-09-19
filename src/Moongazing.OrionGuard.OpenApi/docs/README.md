# OrionGuard.OpenApi

OpenAPI-first validation for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). A Roslyn incremental source generator reads an OpenAPI 3 JSON document at compile time and emits an OrionGuard `IValidator<T>` that enforces the constraints of one schema. It is the inverse of `OrionGuard.Swagger`, which describes validators in OpenAPI.

## Install

```bash
dotnet add package OrionGuard
dotnet add package OrionGuard.OpenApi
```

`OrionGuard.OpenApi` is a development dependency: it runs inside the compiler, adds no assembly to your output, and does not flow to projects that reference yours. The generated code uses `IValidator<T>` and `GuardResult` from the core `OrionGuard` package, so reference that package too.

## Quick start

Pass the document to the compiler as an additional file:

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi.json" />
</ItemGroup>
```

Mark a partial class that implements `IValidator<T>`:

```csharp
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.OpenApi;

// Validate and ValidateAsync are generated.
GuardResult result = new CustomerValidator().Validate(new Customer { Email = "a@b.co", Age = 30 });

[OpenApiValidator("openapi.json", "#/components/schemas/Customer")]
public partial class CustomerValidator : IValidator<Customer> { }

public sealed class Customer
{
    public string? Email { get; set; }
    public int Age { get; set; }
}
```

The generated class is an ordinary `IValidator<T>`, so it works anywhere a hand-written OrionGuard validator does: the ASP.NET Core endpoint filter, the MediatR behavior, or a direct call.

## How the target is bound

- The target must be a `partial class`; records are not picked up. The generated partial repeats the class's accessibility.
- `T` comes from the `IValidator<T>` the class implements. Implement it directly. The generator also finds `T` through an `AbstractValidator<T>` or `FluentStyleValidator<T>` base, but those bases already declare `Validate` and `ValidateAsync`, which the generated members then hide (compiler warning CS0108). A class without `IValidator<T>` gets OG1008 and no code.
- Nested targets work when the target and every enclosing type are `partial` (otherwise OG1011). Generic targets, and targets nested in a generic type, are skipped with OG1010.
- Schema properties bind to public, readable instance properties of `T`, including inherited ones, by case-insensitive name, so `firstName` binds to `FirstName`. A schema property with no matching C# property is skipped without a diagnostic.
- The document argument is matched against the additional files by path suffix first, so `specs/openapi.json` picks that file. A bare file name falls back to a file-name match. A name that fits more than one file raises OG1009.

## Supported keywords

Which checks are emitted depends on the C# property type, not on the schema's `type` keyword. A keyword that does not fit the property type is skipped.

| Keyword | C# property type | Check and error code |
| --- | --- | --- |
| `required` | Reference type or `Nullable<T>` | Not null (`REQUIRED`). Value-type properties have no check. |
| `minLength` / `maxLength` | `string` | Length bounds (`MIN_LENGTH` / `MAX_LENGTH`) |
| `pattern` | `string` | `Regex.IsMatch` (`PATTERN`) |
| `format` | `string` | `email`, `uuid`, `date-time`, `date`, `uri`, `hostname`, `ipv4` by regular expression (`FORMAT`). Other formats are ignored. |
| `enum` | `string` or numeric | Allowed values; strings compare ordinally (`ENUM`) |
| `minimum` / `maximum` | Numeric | Range (`MINIMUM` / `MAXIMUM`). Integral and `decimal` properties compare as `decimal`, `float` and `double` as `double`. |
| `exclusiveMinimum` / `exclusiveMaximum` | Numeric | Boolean form (OpenAPI 3.0) and numeric form (3.1) |
| `minItems` / `maxItems` | Array or `IEnumerable` | Item count (`MIN_ITEMS` / `MAX_ITEMS`) |
| `$ref` | Any | Same-document references (`#/...`) are resolved for the root pointer and for each property |

A null property skips every check except `required`. A null instance returns a single `NULL` error. Errors are keyed by the schema's property name.

## Not supported

- **YAML documents.** Only JSON is read; YAML raises OG1002. Convert the document to JSON.
- **Composition and polymorphism** (`discriminator`, `oneOf`, `anyOf`, `allOf`). The generator raises OG1006, skips the construct, and enforces the rest of the schema.
- **Nested structures.** `items` schemas and the properties of nested objects are not validated; only the top-level properties of the target schema are.
- **`type` and `nullable`.** Both are read but do not change the generated checks.
- **References to other files.** Only same-document pointers are resolved.

## Diagnostics

Problems in the document or the target are reported as diagnostics. The generator then skips the affected target, or for OG1006 and OG1007 only the affected construct or constraint. Error-severity diagnostics fail the build.

| Id | Severity | Meaning |
| --- | --- | --- |
| `OG1001` | Error | The named document is not among the additional files. |
| `OG1002` | Error | The document could not be parsed as JSON (YAML is not supported). |
| `OG1003` | Error | The JSON pointer did not resolve to a schema. |
| `OG1004` | Error | A `$ref` could not be resolved, or is part of a cycle. |
| `OG1005` | Warning | The target is not a `partial` class; nothing was generated. |
| `OG1006` | Warning | An unsupported construct was skipped; the rest of the schema is still enforced. |
| `OG1007` | Warning | `minLength`, `maxLength`, `minItems`, or `maxItems` is not an integer or is outside the `int` range; that constraint was skipped. |
| `OG1008` | Warning | The target does not implement `IValidator<T>`, so the validated type could not be inferred; nothing was generated. |
| `OG1009` | Error | The document name matched more than one additional file. |
| `OG1010` | Warning | The target is generic or nested in a generic type; nothing was generated. |
| `OG1011` | Warning | A nested target or one of its enclosing types is not `partial`; nothing was generated. |
| `OG1012` | Warning | An enclosing type is of a kind that cannot be reopened as `partial`; nothing was generated. |

## Targets

- The generator targets `netstandard2.0` and runs in the compiler, so it works with any consuming target framework that the `OrionGuard` package supports (`net8.0`, `net9.0`, `net10.0`).
- It is built against `Microsoft.CodeAnalysis.CSharp` 5.9.0, so it needs the .NET SDK 10.0.400 or newer. Older SDKs, including every .NET 8 and .NET 9 SDK, report CS9057 and skip the generator.
- The JSON reader is bundled; the package has no other dependencies.

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard) (`IValidator<T>` and `GuardResult`), [OrionGuard.Swagger](https://www.nuget.org/packages/OrionGuard.Swagger) (validators to OpenAPI), [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) (endpoint validation)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
