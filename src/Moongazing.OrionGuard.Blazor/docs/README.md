# OrionGuard.Blazor

Blazor `EditForm` validation components for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). `<OrionGuardValidator />` validates the form model with OrionGuard attributes, and `<OrionGuardFluentValidator TModel="..." />` validates it with a registered `IValidator<TModel>`.

## Install

```bash
dotnet add package OrionGuard.Blazor
```

The core `OrionGuard` package is installed as a dependency.

## Quick start

Register the services and a validator in `Program.cs`:

```csharp
using Moongazing.OrionGuard.Blazor.Extensions;
using Moongazing.OrionGuard.DependencyInjection;

builder.Services.AddOrionGuardBlazor();
builder.Services.AddValidator<SignUpModel, SignUpModelValidator>();

public sealed class SignUpModel
{
    public string Email { get; set; } = "";
}

public sealed class SignUpModelValidator : AbstractValidator<SignUpModel>
{
    public SignUpModelValidator()
    {
        RuleFor(x => x.Email, nameof(SignUpModel.Email), p => p.NotEmpty().Email());
    }
}
```

Use the component inside an `EditForm`:

```razor
@using Moongazing.OrionGuard.Blazor

<EditForm Model="model" OnValidSubmit="Save">
    <OrionGuardFluentValidator TModel="SignUpModel" />

    <InputText @bind-Value="model.Email" />
    <ValidationMessage For="() => model.Email" />

    <button type="submit">Save</button>
</EditForm>

@code {
    private readonly SignUpModel model = new();

    private void Save() { }
}
```

`AddOrionGuardBlazor()` only calls `AddOrionGuard()`. It does not register validators.

## Components

`<OrionGuardValidator />`

- Validates `EditContext.Model` with `AttributeValidator`, using the attributes from `Moongazing.OrionGuard.Attributes`: `[NotNull]`, `[NotEmpty]`, `[Length]`, `[Email]`, `[Range]`, `[Regex]`, `[Positive]`.
- Needs no DI registration.
- Does not evaluate `System.ComponentModel.DataAnnotations` attributes. Use `<DataAnnotationsValidator />` for those.

`<OrionGuardFluentValidator TModel="..." />`

- Injects `IValidator<TModel>`, so a validator for the model type must be registered.
- Calls the synchronous `Validate(model)`, so `RuleForAsync` rules do not run. This is deliberate: `EditContext.Validate()` raises its validation events synchronously and returns the result at once, so an async rule could not finish before the form decides whether it is valid. If a form depends on async rules, inject `IValidator<TModel>` and await `ValidateAsync` in the submit handler.

## Behaviour

- Both components need a cascading `EditContext`, so place them inside an `EditForm`. Otherwise they throw `InvalidOperationException`.
- On submit, all messages are cleared and the whole model is validated.
- When a field changes, the whole model is validated again, but only the errors for that field are shown.
- Errors are attached to `FieldIdentifier(model, error.ParameterName)`. The parameter name must match the property name for `<ValidationMessage For="..." />` to show the message. Errors for nested objects do not map to a field, but they still appear in `<ValidationSummary />`.

## Hosting

The package references the `Microsoft.AspNetCore.App` shared framework, so it is meant for server-hosted Blazor (Blazor Server or interactive server rendering). A Blazor WebAssembly client project cannot reference that shared framework.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- Razor class library using the `Microsoft.AspNetCore.App` shared framework

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard), [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
