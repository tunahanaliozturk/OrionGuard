# OrionGuard.SignalR

A SignalR hub filter for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). It validates hub method arguments with your `IValidator<T>` registrations and rejects invalid invocations with a `HubException`.

## Install

```bash
dotnet add package OrionGuard.SignalR
```

The package uses the ASP.NET Core shared framework, which includes SignalR. The core `OrionGuard` package is installed as a dependency.

## Quick start

```csharp
using Microsoft.AspNetCore.SignalR;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.SignalR;

builder.Services.AddOrionGuardSignalR();
builder.Services.AddValidator<ChatMessage, ChatMessageValidator>();

var app = builder.Build();
app.MapHub<ChatHub>("/chat");

public sealed record ChatMessage(string Text);

public sealed class ChatMessageValidator : AbstractValidator<ChatMessage>
{
    public ChatMessageValidator()
    {
        RuleFor(x => x.Text, nameof(ChatMessage.Text), p => p.NotEmpty().Length(1, 500));
    }
}

public sealed class ChatHub : Hub
{
    public Task Send(ChatMessage message) => Clients.All.SendAsync("receive", message);
}
```

`AddOrionGuardSignalR()` registers `OrionGuardHubFilter` as a singleton, calls `AddSignalR()`, and adds the filter to every hub. Do not add the filter again with `options.AddFilter<OrionGuardHubFilter>()`, or it will run twice. The package does not scan assemblies, so register each validator yourself.

## Behaviour

- For each non-null argument, the filter resolves `IValidator<T>` for the argument's runtime type. Arguments without a registered validator are skipped.
- Errors from all arguments are collected. If there are any, the filter throws `HubException` with the message `Validation failed: <messages>`, where the error messages are joined with `"; "`. SignalR sends a `HubException` message to the calling client, and the connection stays open.
- The error arrives as a single message string, not as structured field errors.
- The filter calls the synchronous `Validate`, so `RuleForAsync` rules do not run.
- The filter is a singleton and resolves validators from the root service provider. Validators must not be registered as scoped, and must not depend on scoped services.

## Known issue

In this version the filter looks up `Validate` with `Type.GetMethod("Validate")`. That lookup is ambiguous because `IValidator<T>` declares two `Validate` overloads. As a result, any invocation with an argument that has a registered validator fails with `AmbiguousMatchException`, and the client sees a generic invocation error instead of the validation result. Invocations whose arguments have no registered validator are not affected. Until this is fixed, validate inside the hub method and throw `HubException` yourself.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- Uses the ASP.NET Core shared framework (`Microsoft.AspNetCore.App`); no other package dependencies

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard), [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
