# OrionGuard.Blazor

Blazor integration for [**OrionGuard**](https://github.com/tunahanaliozturk/OrionGuard). Plugs OrionGuard into Blazor's `EditForm` so server-side validators drive the client UI without duplicating rules.

## What this package adds

- **`<OrionGuardValidator />`** component — drop it inside an `<EditForm>` and validation delegates to your registered `IValidator<TModel>`.
- **Data-annotations interop** — existing `[Required]` / `[EmailAddress]` / `[StringLength]` attributes still work alongside OrionGuard attributes.
- **Per-field messages** pair with `<ValidationMessage For="() => Model.Property" />` out of the box.
- Works in **Blazor Server**, **Blazor WebAssembly**, and **Blazor Hybrid** apps.

## Install

```bash
dotnet add package OrionGuard.Blazor
```

The core `OrionGuard` package is brought in transitively.

## Quick start

```razor
@using Moongazing.OrionGuard.Blazor

<EditForm Model="@user" OnValidSubmit="HandleSubmit">
    <OrionGuardValidator />

    <InputText @bind-Value="user.Name" />
    <ValidationMessage For="() => user.Name" />

    <InputText @bind-Value="user.Email" />
    <ValidationMessage For="() => user.Email" />

    <button type="submit">Save</button>
</EditForm>

@code {
    private readonly User user = new();

    private void HandleSubmit() { /* ... */ }
}
```

Any `IValidator<User>` registered in DI will be executed by `<OrionGuardValidator />`.

## Targets

.NET 8.0, .NET 9.0, .NET 10.0.

## License

MIT. See the [main repository](https://github.com/tunahanaliozturk/OrionGuard) for full docs, CHANGELOG, and samples.
