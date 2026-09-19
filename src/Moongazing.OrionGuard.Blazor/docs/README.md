# OrionGuard.Blazor

Runs your OrionGuard rules inside an `EditForm`, so the same validator the API uses also drives the form's field messages.

```bash
dotnet add package OrionGuard.Blazor
```

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

Typing an invalid address and leaving the field puts the validator's message under that input; submitting with it invalid never calls `Save`. The core `OrionGuard` package comes along as a dependency.

The component resolves `IValidator<SignUpModel>` from DI, so register it:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Blazor.Extensions;
using Moongazing.OrionGuard.DependencyInjection;

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

public static class BlazorSetup
{
    public static void Add(IServiceCollection services) =>
        services
            .AddOrionGuardBlazor()
            .AddValidator<SignUpModel, SignUpModelValidator>();
}
```

`AddOrionGuardBlazor()` only calls `AddOrionGuard()`; it registers no validators.

## The two components

**`<OrionGuardFluentValidator TModel="..." />`** injects `IValidator<TModel>` and runs it. A validator for the model type must be registered, or the component cannot be constructed.

**`<OrionGuardValidator />`** needs no registration at all: it validates `EditContext.Model` with `AttributeValidator`, using the attributes from `Moongazing.OrionGuard.Attributes` — `[NotNull]`, `[NotEmpty]`, `[Length]`, `[Email]`, `[Range]`, `[Regex]`, `[Positive]`.

Both react to the same `EditContext` events: on submit the messages are cleared and the whole model is validated; when one field changes the whole model is validated again but only that field's errors are shown. An error is attached to `FieldIdentifier(model, error.ParameterName)`.

## What this does not do

- **No async rules in the form.** `OrionGuardFluentValidator` calls the synchronous `Validate(model)`, so `RuleForAsync` rules never run here. That is forced by Blazor: `EditContext.Validate()` raises its events synchronously and returns a verdict immediately, so an async rule could not finish in time. For a "is this email taken" check, inject `IValidator<TModel>` into the page and `await ValidateAsync(...)` in the submit handler.
- **`<OrionGuardValidator />` ignores `System.ComponentModel.DataAnnotations`.** `[Required]` and friends are not read; add `<DataAnnotationsValidator />` alongside it if your model uses both.
- **The error's `ParameterName` must equal the property name** for `<ValidationMessage For="..." />` to find it. An error for a nested object does not map to a field — it still shows in `<ValidationSummary />`, just not next to an input.
- **Both components require a cascading `EditContext`.** Outside an `EditForm` they throw `InvalidOperationException`.
- **Server-hosted Blazor only.** The package references the `Microsoft.AspNetCore.App` shared framework, which a Blazor WebAssembly client project cannot reference. Use it from Blazor Server or interactive server rendering.

## Targets

`net8.0`, `net9.0`, `net10.0`. A Razor class library on the ASP.NET Core shared framework.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) · [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) (the same validator behind the API the form posts to)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
