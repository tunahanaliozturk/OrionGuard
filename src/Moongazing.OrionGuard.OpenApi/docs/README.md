# OrionGuard.OpenApi

Makes the OpenAPI document the source of truth: a source generator reads one schema out of it at compile time and emits an `IValidator<T>` that enforces exactly what the contract says.

```bash
dotnet add package OrionGuard
dotnet add package OrionGuard.OpenApi
```

Hand the document to the compiler:

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi.json" />
</ItemGroup>
```

Then point a partial class at a schema:

```csharp
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.OpenApi;

[OpenApiValidator("openapi.json", "#/components/schemas/Customer")]
public partial class CustomerValidator : IValidator<Customer> { }

public sealed class Customer
{
    public string? Email { get; set; }
    public int Age { get; set; }
}

public static class CheckCustomer
{
    // Validate and ValidateAsync are generated; nothing else is needed.
    public static GuardResult Run(Customer customer) => new CustomerValidator().Validate(customer);
}
```

You get an ordinary `IValidator<Customer>` — usable by the ASP.NET Core endpoint filter, the MediatR behavior, or a direct call — whose rules cannot drift from the document, because they are compiled from it on every build. `OrionGuard.OpenApi` runs inside the compiler, adds no assembly to your output and does not flow to projects that reference yours. This is the inverse of [OrionGuard.Swagger](https://www.nuget.org/packages/OrionGuard.Swagger), which writes validator constraints *into* a document.

## How the target is bound

- The target must be a `partial class`; a record is not picked up (OG1005 for a non-partial one). The generated partial repeats the class's accessibility.
- `T` comes from the `IValidator<T>` the class implements — implement it directly. The generator will also find `T` through an `AbstractValidator<T>` or `FluentStyleValidator<T>` base, but those already declare `Validate` and `ValidateAsync`, so the generated members hide them (CS0108). A class implementing neither gets OG1008 and no code.
- Schema properties bind to public, readable instance properties of `T`, inherited ones included, by case-insensitive name: `firstName` binds to `FirstName`. A schema property with no C# counterpart is skipped silently.
- The document name is matched against the additional files by path suffix first, so `specs/openapi.json` selects that file; a bare file name falls back to a file-name match, and an ambiguous name raises OG1009.

## Which keywords become checks

What is emitted depends on the **C# property type**, not on the schema's `type` keyword. A keyword that does not fit the property type is skipped.

| Keyword | C# property type | Check (error code) |
| --- | --- | --- |
| `required` | Reference type or `Nullable<T>` | Not null (`REQUIRED`). A non-nullable value type has no check. |
| `minLength` / `maxLength` | `string` | Length bounds (`MIN_LENGTH` / `MAX_LENGTH`) |
| `pattern` | `string` | Regular-expression match (`PATTERN`) |
| `format` | `string` | `email`, `uuid`, `date-time`, `date`, `uri`, `hostname`, `ipv4` (`FORMAT`); other formats ignored |
| `enum` | `string` or numeric | Allowed values; strings compare ordinally (`ENUM`) |
| `minimum` / `maximum` | Numeric | Range (`MINIMUM` / `MAXIMUM`). Integral and `decimal` compare as `decimal`, `float` as `float`, `double` as `double`. |
| `exclusiveMinimum` / `exclusiveMaximum` | Numeric | Both the boolean form (OpenAPI 3.0) and the numeric form (3.1) |
| `minItems` / `maxItems` | Array or `IEnumerable` | Item count (`MIN_ITEMS` / `MAX_ITEMS`) |
| `$ref` | Any | Same-document references (`#/...`), for the root pointer and each property |

A null property skips every check but `required`; a null instance returns a single `NULL` error. Errors are keyed by the schema's property name.

The `pattern` and `format` checks go through the core package's `RegexCache` with a one-second match timeout, and the built-in format patterns anchor at the true end of the value (a trailing newline fails) and accept ASCII digits only. A match that times out — a backtracking `pattern` on hostile input — adds the `PATTERN` or `FORMAT` error rather than hanging the request thread.

## Diagnostics

The generator skips the affected target, or for OG1006/OG1007 only the affected construct. Error-severity diagnostics fail the build.

| Id | Severity | Meaning |
| --- | --- | --- |
| `OG1001` | Error | The named document is not among the additional files. |
| `OG1002` | Error | The document is not parseable JSON, or nests more than 256 levels deep. |
| `OG1003` | Error | The JSON pointer did not resolve to a schema. |
| `OG1004` | Error | A `$ref` could not be resolved, or is part of a cycle. |
| `OG1005` | Warning | The target is not a `partial` class. |
| `OG1006` | Warning | An unsupported construct was skipped; the rest of the schema is still enforced. |
| `OG1007` | Warning | A length or item bound is not an integer, or is outside `int`; that constraint was skipped. |
| `OG1008` | Warning | The target does not implement `IValidator<T>`, so `T` could not be inferred. |
| `OG1009` | Error | The document name matched more than one additional file. |
| `OG1010` | Warning | The target is generic or nested in a generic type. |
| `OG1011` | Warning | A nested target, or one of its enclosing types, is not `partial`. |
| `OG1012` | Warning | An enclosing type is of a kind that cannot be reopened as `partial`. |

## What this does not do

- **JSON only.** A YAML document raises OG1002. Convert it, or emit JSON from your pipeline.
- **No composition or polymorphism.** `allOf`, `oneOf`, `anyOf` and `discriminator` raise OG1006, are skipped, and the rest of the schema is still enforced — so a validator can silently be weaker than the document reads.
- **Top-level properties only.** `items` schemas and the properties of nested objects are not validated; `minItems` counts a collection, it does not look inside it.
- **`type` and `nullable` are read but change nothing.** The C# property type decides which checks exist, so a schema saying `type: string` on an `int` property yields no string checks rather than an error.
- **Same-document `$ref` only.** A pointer into another file does not resolve (OG1004).
- **It needs a recent SDK.** Built against Roslyn 5.9, so the build SDK must be .NET SDK 10.0.400 or newer; an older one reports CS9057 and skips the generator, leaving `Validate` unimplemented and the build broken.

## Targets

`netstandard2.0`, as Roslyn components must be; consuming projects target `net8.0`, `net9.0` or `net10.0`. The JSON reader is bundled, so the package has no other dependencies.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) (`IValidator<T>` and `GuardResult`) · [OrionGuard.Swagger](https://www.nuget.org/packages/OrionGuard.Swagger) (validators to OpenAPI) · [OrionGuard.Generators](https://www.nuget.org/packages/OrionGuard.Generators) (validators from attributes) · [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
