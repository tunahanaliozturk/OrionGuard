# OrionGuard.Swagger

A Swashbuckle schema filter for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). It reads the OrionGuard validation attributes on your models and adds the matching constraints to the generated OpenAPI schema.

## Install

```bash
dotnet add package OrionGuard.Swagger
```

The package depends on `Swashbuckle.AspNetCore.SwaggerGen` 10.x, which uses Microsoft.OpenApi 2.x and the OpenAPI 3.1 object model. Your application must use Swashbuckle.AspNetCore 10; Swashbuckle 6.x through 9.x are not compatible. The core `OrionGuard` package is installed as a dependency.

## Quick start

```csharp
using Moongazing.OrionGuard.Swagger;

builder.Services.AddSwaggerGen();
builder.Services.AddOrionGuardSwagger();
```

`AddOrionGuardSwagger()` registers `OrionGuardSchemaFilter` with `services.Configure<SwaggerGenOptions>(...)`. It does not call `AddSwaggerGen()`, so keep your existing Swashbuckle setup. If you prefer to register the filter yourself, use `options.SchemaFilter<OrionGuardSchemaFilter>()` inside `AddSwaggerGen` instead.

Annotate models with the attributes from `Moongazing.OrionGuard.Attributes`:

```csharp
using Moongazing.OrionGuard.Attributes;

public sealed class CreateUserRequest
{
    [NotNull, Email] public string Email { get; set; } = "";
    [Length(8, 100)] public string Password { get; set; } = "";
    [Range(13, 120)] public int Age { get; set; }
    [Regex("^[a-z0-9-]+$")] public string? Slug { get; set; }
    [Positive] public decimal Budget { get; set; }
}
```

## Attribute mapping

| Attribute | Schema change |
| --- | --- |
| `[NotNull]` | Adds the property to the parent schema's `required` list and removes `null` from the property's `type` |
| `[NotEmpty]` | `minLength: 1` |
| `[Length(min, max)]` | `minLength`, `maxLength` |
| `[Range(min, max)]` | `minimum`, `maximum` |
| `[Email]` | `format: email` |
| `[Regex(pattern)]` | `pattern` |
| `[Positive]` | `exclusiveMinimum: 0` (the numeric form used by OpenAPI 3.1 and JSON Schema 2020-12) |

## Behaviour details

- A property whose schema is a `$ref` to a shared component only gets `required` (from `[NotNull]`). The component schema itself is never modified.
- Properties are matched by the camelCase form of the CLR property name. A property renamed with `[JsonPropertyName]` or a different naming policy is not matched and is left unchanged.
- `[NotEmpty]` always sets `minLength`, including on collection properties. It does not set `minItems`.
- Only public instance properties of object schemas are inspected, and only the seven attributes above are recognised.
- The filter only annotates model schemas. It does not add response schemas or a `ProblemDetails` schema.
- `Range` and `Length` also exist in `System.ComponentModel.DataAnnotations`. If a file imports both namespaces, qualify the OrionGuard attribute or use a `using` alias.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- `Swashbuckle.AspNetCore.SwaggerGen` 10.2.3 or later 10.x (brings Microsoft.OpenApi 2.x)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages:
  - [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) returns validation failures as `ProblemDetails` at runtime.
  - [OrionGuard.OpenApi](https://www.nuget.org/packages/OrionGuard.OpenApi) works in the opposite direction and generates OrionGuard validators from an OpenAPI document.

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
