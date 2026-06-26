# OrionGuard.OpenApi

OpenAPI-first validation for [**OrionGuard**](https://github.com/tunahanaliozturk/OrionGuard). A Roslyn incremental source generator that reads an OpenAPI 3 document at compile time and emits an OrionGuard validator enforcing the referenced schema's constraints. The inverse of `OrionGuard.Swagger`, which goes validator to OpenAPI.

## What this package generates

Mark a `partial class` with `[OpenApiValidator(document, pointer)]`. The generator resolves the schema named by the JSON pointer in the OpenAPI document (supplied as an `AdditionalFile`) and emits the class body implementing `IValidator<T>`.

```csharp
using Moongazing.OrionGuard.DependencyInjection;

[OpenApiValidator("openapi.json", "#/components/schemas/Customer")]
public partial class CustomerValidator : IValidator<Customer> { }

// generated: CustomerValidator.Validate(Customer) -> GuardResult
var result = new CustomerValidator().Validate(customer);
if (result.IsInvalid)
{
    return Results.ValidationProblem(result.ToErrorDictionary());
}
```

The generated validator is an ordinary `IValidator<T>`, so it drops straight into the OrionGuard
ASP.NET Core endpoint filter, the MediatR behaviour, or a manual call exactly like a hand-written one.

## Wiring up the document

The OpenAPI document is passed to the compiler as an `AdditionalFile`. The attribute's first argument is matched against the additional files by file name (a relative path suffix also matches).

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi.json" />
</ItemGroup>
```

The validated type `T` is inferred from the `IValidator<T>` interface (or an `AbstractValidator<T>` /
`FluentStyleValidator<T>` base). If the annotated class has neither, its own properties are validated.
Schema property names bind to C# members case-insensitively, so `firstName` maps to `FirstName`.

## Supported constraints

| OpenAPI keyword | Enforced as |
|-----------------|-------------|
| `type` (string / integer / number / boolean / array / object) | category gate for the other checks |
| `required` | non-null check on the bound member (`REQUIRED`) |
| `nullable` | a null value skips the value constraints |
| `minLength` / `maxLength` | string length bounds (`MIN_LENGTH` / `MAX_LENGTH`) |
| `pattern` | regex match (`PATTERN`) |
| `format` | `email`, `uuid`, `date-time`, `date`, `uri`, `hostname`, `ipv4` (`FORMAT`) |
| `minimum` / `maximum` | numeric bounds, including `exclusiveMinimum` / `exclusiveMaximum` (`MINIMUM` / `MAXIMUM`) |
| `enum` | allowed string or numeric values (`ENUM`) |
| `minItems` / `maxItems` | array bounds (`MIN_ITEMS` / `MAX_ITEMS`) |
| `$ref` | intra-document references are resolved (root pointer and per-property) |

## Not yet supported

- **YAML documents.** Only JSON OpenAPI documents are read; a YAML document raises `OG1002`. Convert the document to JSON, or track the YAML follow-up. (Avoiding a heavy, unbundleable YAML dependency in the analyzer is a deliberate choice.)
- **Polymorphism and composition** (`discriminator`, `oneOf`, `anyOf`, `allOf`). The generator raises `OG1006` and enforces the rest of the schema rather than half-implementing polymorphic dispatch.
- **Generic target types.** A generic `[OpenApiValidator]` target, or a target nested inside a generic type, raises `OG1010` and is skipped. Reconstructing the partial's type parameters and constraints correctly is a follow-up; move the validator to a non-generic type for now. (Non-generic nested targets are supported: the generated partial is emitted inside the correct enclosing types.)

## Diagnostics

Every failure is a non-fatal, OG-prefixed diagnostic; the build never crashes and the generated code stays `TreatWarningsAsErrors`-clean.

| Id | Severity | Meaning |
|----|----------|---------|
| `OG1001` | Error | The named OpenAPI document was not supplied as an `AdditionalFile`. |
| `OG1002` | Error | The document could not be parsed as JSON (YAML is not supported yet). |
| `OG1003` | Error | The JSON pointer did not resolve to a schema. |
| `OG1004` | Error | A `$ref` inside the document could not be resolved. |
| `OG1005` | Warning | The target is not a `partial class`, so no validator was generated. |
| `OG1006` | Warning | An unsupported construct was skipped; the rest of the schema was still enforced. |
| `OG1010` | Warning | The target is generic (or nested inside a generic type), which is not supported yet; no validator was generated. |

## Install

```bash
dotnet add package OrionGuard.OpenApi
```

This package is an analyzer/source-generator reference. It contributes generated code at compile time, bundles its own minimal JSON reader, and adds no runtime dependency to your output.

## Targets

netstandard2.0 (Roslyn-compatible). Consumes any `net8.0+` project.

## License

MIT. See the [main repository](https://github.com/tunahanaliozturk/OrionGuard) for full docs, CHANGELOG, and samples.
