# Moongazing.OrionGuard.Swagger

Swagger / OpenAPI integration for [**OrionGuard**](https://github.com/tunahanaliozturk/OrionGuard). Surfaces your validation attributes directly in the generated OpenAPI schema so API consumers see the same constraints your server enforces.

## What this package adds

- **Schema filters** that read `OrionGuard` validation attributes (`[NotNull]`, `[Email]`, `[Length]`, `[Range]`, `[Pattern]`, etc.) and add them as `required`, `format`, `minLength`, `maxLength`, `minimum`, `maximum`, `pattern` on the corresponding OpenAPI property.
- **ProblemDetails response schema** so clients know what a validation failure looks like (shape: `type`, `title`, `status`, `errors`).
- **Swashbuckle.AspNetCore** compatible out of the box.

## Install

```bash
dotnet add package Moongazing.OrionGuard.Swagger
```

Requires Swashbuckle in your application. The core `OrionGuard` package is brought in transitively.

## Quick start

```csharp
using Moongazing.OrionGuard.Swagger;

builder.Services.AddSwaggerGen(options =>
{
    options.AddOrionGuardSchemaFilters();
});
```

After this call, a DTO like:

```csharp
public sealed class CreateUserRequest
{
    [NotNull, Email] public string Email { get; set; } = default!;
    [Length(8, 100)] public string Password { get; set; } = default!;
    [Range(13, 120)] public int Age { get; set; }
}
```

shows up in `/swagger` with `email` format, `minLength`/`maxLength` on password, and `minimum`/`maximum` on age — automatically.

## Targets

.NET 8.0, .NET 9.0, .NET 10.0.

## License

MIT. See the [main repository](https://github.com/tunahanaliozturk/OrionGuard) for full docs, CHANGELOG, and samples.
