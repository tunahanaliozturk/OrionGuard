# OrionGuard.SignalR

A hub filter that validates hub method arguments with your `IValidator<T>` registrations and rejects a bad invocation with a `HubException`, without dropping the connection.

```bash
dotnet add package OrionGuard.SignalR
```

```csharp
using Microsoft.AspNetCore.SignalR;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrionGuardSignalR();
builder.Services.AddValidator<ChatMessage, ChatMessageValidator>();

var app = builder.Build();
app.MapHub<ChatHub>("/chat");
app.Run();

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

A client invoking `Send` with an empty `Text` gets back a `HubException` whose message reads `Validation failed: Text: <message>`, the hub method does not run, and the connection stays open. The core `OrionGuard` package comes along as a dependency; SignalR itself is in the ASP.NET Core shared framework.

## What the filter does

`AddOrionGuardSignalR()` registers `OrionGuardHubFilter` as a singleton, calls `AddSignalR()`, and adds the filter to every hub.

- For each non-null argument it runs every `IValidator<T>` registered for the argument's **runtime** type. An argument with no registered validator is skipped.
- Validators come from the hub invocation's scope (`HubInvocationContext.ServiceProvider`), so a scoped validator, or one holding a `DbContext`, works.
- They run one after another through `ValidateAsync`, so `RuleForAsync` rules run and two validators never touch a scoped `DbContext` at the same time.
- Errors from all arguments are collected before anything is thrown, listed in argument order and then validator registration order, and joined into `Validation failed: <field>: <message>; <field>: <message>`.

## What this does not do

- **The client gets one string, not structured errors.** SignalR delivers the `HubException` message; there is no per-field error payload on the wire. If your UI needs to highlight fields, send the model to a validating endpoint instead, or put the structure in your own result type.
- **It does not find your validators.** No assembly scanning; register each one with `AddValidator<T, TValidator>()`.
- **Matching is by runtime type, across every hub.** Registering `IValidator<string>` means every `string` argument of every hub method in the process is validated by it. Prefer a message type per hub method.
- **Do not add the filter twice.** `AddOrionGuardSignalR()` already installs it globally; adding `options.AddFilter<OrionGuardHubFilter>()` on top runs it — and your validators — a second time per invocation.
- **Only method arguments are validated.** Values streamed from the client (`IAsyncEnumerable<T>` parameters), values the hub sends to clients, and connection lifetime events are not.

## Targets

`net8.0`, `net9.0`, `net10.0`. Uses the ASP.NET Core shared framework; no package dependencies beyond OrionGuard itself.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) · [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) (the same validators for HTTP endpoints)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
