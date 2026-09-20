# OrionGuard.Generators

Writes the validator for you at compile time: mark a type `[GenerateValidator]`, annotate its properties, and get a static, reflection-free validator — the one you want when you publish trimmed or NativeAOT.

```bash
dotnet add package OrionGuard
dotnet add package OrionGuard.Generators
```

```csharp
namespace MyApp;

using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Generators;

[GenerateValidator]
public sealed class CreateUserRequest
{
    [NotNull, NotEmpty, Length(3, 50)] public string Name { get; set; } = default!;
    [NotNull, Email]                   public string Email { get; set; } = default!;
    [Range(13, 120)]                   public int Age { get; set; }
}
```

The compiler now has a `CreateUserRequestValidator` in the same namespace, with no reflection anywhere in it:

```csharp
namespace MyApp;

using Moongazing.OrionGuard.Core;

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

`OrionGuard.Generators` runs inside the compiler and adds nothing to your output; the generated code uses the attributes and `GuardResult` from the core `OrionGuard` package, so reference both.

## What it generates

A static class `<TypeName>Validator` in the type's namespace, with `GuardResult Validate(<TypeName>)` and `<TypeName> ValidateAndThrow(<TypeName>)`. It is `public` when the type and every type enclosing it are public, `internal` otherwise.

| Attribute | Generated check | Default error code |
| --- | --- | --- |
| `[NotNull]` | value is not null | `NOT_NULL` |
| `[NotEmpty]` | string is not null or whitespace; collection is not null or empty | `NOT_EMPTY` |
| `[Length(min, max)]` | string length within `min`..`max` | `LENGTH` |
| `[Email]` | value looks like an email address | `EMAIL` |
| `[Range(min, max)]` | numeric value within `min`..`max` | `RANGE` |
| `[Positive]` | numeric value greater than zero | `POSITIVE` |
| `[Regex(pattern)]` | value matches the pattern | `REGEX` |

Override either default per attribute: `[Email(ErrorMessage = "Enter a valid email.", ErrorCode = "BAD_EMAIL")]`.

Details worth knowing:

- Null and empty checks run first, and the rest of a property's checks only when those pass, so a null value reports one error rather than four.
- Public instance properties carrying at least one of these attributes are validated, inherited ones included. An override with no attributes keeps the rules of the property it overrides. A type with no annotated property gets no validator.
- `[Range]` bounds are emitted culture-invariant and typed to the property: `decimal` against `decimal` literals, `float` against `float` literals, everything else against `double`; a bound outside `decimal`'s range, such as `double.MaxValue`, compares as `double`. A `NaN` bound rejects every value, matching the reflection-based attribute.
- `[Email]` and `[Regex]` matches carry a one-second timeout. A match that times out adds the `EMAIL` or `REGEX` error instead of throwing.
- The `[GenerateValidator]` attribute is injected into your compilation by the generator, in the `Moongazing.OrionGuard.Generators` namespace.

### Target shapes it handles

Classes and structs, sealed or not, in a namespace or the global namespace. Records — `record`, `record class`, `record struct`; for a positional record target the property explicitly: `public record Signup([property: NotNull, Email] string? Email);`. Nested types, where the validator for `Outer.Inner` is `InnerValidator` at namespace level next to `Outer`, provided the type and every enclosing type are public or internal. Same-named types in different namespaces each get their own validator.

## Diagnostics

| ID | Severity | Meaning |
| --- | --- | --- |
| `OG0001` | Warning | A public property on a `[GenerateValidator]` type has no OrionGuard attribute, so the validator accepts any value for it. |
| `OG0002` | Warning | A property carries a `ValidationAttribute` subclass the generator cannot translate, so the validator does not enforce it. |
| `OG0003` | Warning | A property cannot be read from the generated code (an inherited `{ protected get; set; }`, say) and was skipped; the reflection-based `AttributeValidator` still enforces it. |

Types without `[GenerateValidator]` are never analyzed.

## What this does not do

- **Seven attributes, and nothing else.** Anything outside the table is not translated — your own `ValidationAttribute` subclass raises OG0002, and `System.ComponentModel.DataAnnotations` attributes are ignored outright, because matching is by full type name.
- **No cross-property, conditional or async rules.** One property, one attribute, one check. Rules that compare two properties, hit a database, or depend on a `ValidationContext` belong in an `AbstractValidator<T>` or `Validate.For(...)`.
- **No recursion.** A property whose own type is `[GenerateValidator]`-annotated is not validated through; call the nested type's validator yourself. `[NotEmpty]` on a collection checks that the collection has items, not that the items are valid.
- **Generic types are not supported**, and neither is a type nested in one.
- **Name collisions do not compile.** Two nested types with the same simple name in one namespace, or a nested type and a top-level type with the same name, produce two validators with the same name. Rename one.
- **It needs a recent SDK.** The generator is built against Roslyn 5.9, so the SDK must ship at least that — .NET SDK 10.0.400 or newer. An older SDK, including every .NET 8 and .NET 9 SDK, reports CS9057 and *skips the generator*: the `<TypeName>Validator` classes are simply not there and your code does not compile. Your project can still target `net8.0`, `net9.0` or `net10.0`; it is the build SDK that matters.

## Deprecated: `[StronglyTypedId<TValue>]`

The strongly-typed id generator in this package is superseded by the standalone [OrionKey](https://github.com/tunahanaliozturk/OrionKey) package (`[OrionId<TValue>]`). The attribute is `[Obsolete]`, so every use warns CS0618, and it is removed in the next major version. Only the generator is deprecated — the hand-written `StronglyTypedId<TValue>` record in the core `OrionGuard` package is not.

While it is still here: on a `readonly partial struct`, `[StronglyTypedId<TValue>]` (namespace `Moongazing.OrionGuard.Domain.Primitives`) generates the struct body (`Value`, constructor, equality, `IStronglyTypedId<TValue>`), a `System.Text.Json` converter, a `TypeConverter`, `IParsable`/`ISpanParsable`, and an EF Core `ValueConverter` when the project references EF Core. `Guid`, `int`, `long` and `string` are supported, and `services.AddOrionGuardStronglyTypedIds()` registers the generated EF Core converters.

Two things to know about it: the struct carries `[TypeConverter]` for the generated converter (yours is kept if you applied one), so ASP.NET Core model binding finds it — but the generated `<TypeName>JsonConverter` is **not** attached, so `System.Text.Json` keeps writing `{"Value":"abc"}`, a shape it cannot read back into the id. Add the converter to `JsonSerializerOptions.Converters` for a bare-value round trip.

Moving to OrionKey:

| OrionGuard.Generators | OrionKey |
| --- | --- |
| `using Moongazing.OrionGuard.Domain.Primitives;` | `using Moongazing.OrionKey;` |
| `[StronglyTypedId<Guid>]` | `[OrionId<Guid>]` |
| `[StronglyTypedId<int>]` | `[OrionId<int>]` |

The [migration guide](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/stronglytypedid-to-orionkey.md) covers `long` and `string` ids and the remaining differences.

## Targets

The package targets `netstandard2.0`, as Roslyn components must. Consuming projects target `net8.0`, `net9.0` or `net10.0`.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) (required) · [OrionGuard.OpenApi](https://www.nuget.org/packages/OrionGuard.OpenApi) (a validator generated from an OpenAPI schema instead of attributes) · [OrionKey](https://github.com/tunahanaliozturk/OrionKey)

## Documentation

- [OrionGuard README](https://github.com/tunahanaliozturk/OrionGuard#readme)
- [CHANGELOG](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
