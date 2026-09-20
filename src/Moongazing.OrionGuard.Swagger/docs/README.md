# OrionGuard.Swagger

Publishes the rules you already enforce: a Swashbuckle schema filter that turns the OrionGuard attributes on your models into OpenAPI constraints, so the documented contract matches the one the server checks.

```bash
dotnet add package OrionGuard.Swagger
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Swagger;

public sealed class CreateUserRequest
{
    [NotNull, Email] public string Email { get; set; } = "";
    [Length(8, 100)] public string Password { get; set; } = "";
    [Range(13, 120)] public int Age { get; set; }
    [Regex("^[a-z0-9-]+$")] public string? Slug { get; set; }
    [Positive] public decimal Budget { get; set; }
}

public static class SwaggerSetup
{
    public static void Add(IServiceCollection services)
    {
        services.AddSwaggerGen();
        services.AddOrionGuardSwagger();
    }
}
```

The generated document now carries `required: [email]`, `format: email`, `minLength`/`maxLength` on `password`, `minimum`/`maximum` on `age`, `pattern` on `slug` and `exclusiveMinimum: 0` on `budget` — so client generators and API explorers show the same limits the request will be held to. The core `OrionGuard` package comes along as a dependency.

`AddOrionGuardSwagger()` registers `OrionGuardSchemaFilter` through `services.Configure<SwaggerGenOptions>(...)`. It does not call `AddSwaggerGen()`, so your existing Swashbuckle setup stays as it is; `options.SchemaFilter<OrionGuardSchemaFilter>()` inside `AddSwaggerGen` does the same job if you prefer to wire it there.

## What each attribute writes

| Attribute | Schema change |
| --- | --- |
| `[NotNull]` | Adds the property to the parent schema's `required`, and removes `null` from the property's `type` |
| `[NotEmpty]` | `minLength: 1` |
| `[Length(min, max)]` | `minLength`, `maxLength` |
| `[Range(min, max)]` | `minimum`, `maximum`, written with the invariant culture |
| `[Email]` | `format: email` |
| `[Regex(pattern)]` | `pattern` |
| `[Positive]` | `exclusiveMinimum: 0` — the numeric form OpenAPI 3.1 and JSON Schema 2020-12 use |

## What this does not do

- **It reads OrionGuard attributes only.** `System.ComponentModel.DataAnnotations` attributes are not looked at, and neither is a `ValidationAttribute` of your own that is not one of the seven above — it is skipped in silence. If a file imports both namespaces, `Range` and `Length` are ambiguous; qualify them or add a `using` alias.
- **It documents attributes, not validators.** Rules written in an `AbstractValidator<T>`, in `Validate.For(...)`, or as `RuleForAsync` never reach the schema. Only what is on the model does.
- **A `$ref` property gets `required` and nothing else.** When a property's schema is a reference to a shared component, the component is left untouched — changing it would affect every other use of that type.
- **Renamed properties are skipped.** Matching is on the camelCase form of the CLR property name; a property renamed with `[JsonPropertyName]`, or by a naming policy other than camelCase, is not found and is left unchanged.
- **`[NotEmpty]` always writes `minLength`**, including on a collection property. It never writes `minItems`.
- **It only touches model schemas.** No response schemas, no `ProblemDetails` schema; [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) produces those at runtime, and you document them the usual Swashbuckle way.

## Targets

`net8.0`, `net9.0`, `net10.0`; `Swashbuckle.AspNetCore.SwaggerGen` 10.x, which brings Microsoft.OpenApi 2.x and the OpenAPI 3.1 object model. Swashbuckle 9 and earlier are not compatible — the filter implements the 10.x `Apply(IOpenApiSchema, ...)` signature.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) · [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) (answers a failed request with `ProblemDetails`) · [OrionGuard.OpenApi](https://www.nuget.org/packages/OrionGuard.OpenApi) — the same idea in reverse, generating a validator from an OpenAPI document

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
