# OrionGuard.SignalR

SignalR integration for [**OrionGuard**](https://github.com/tunahanaliozturk/OrionGuard). Plugs a hub filter into the SignalR pipeline so every hub method parameter is validated by your OrionGuard validators before the method body runs.

## What this package adds

- **`OrionGuardHubFilter`** — a SignalR `IHubFilter` that resolves the appropriate `IValidator<T>` for each hub method argument and runs it before invocation.
- **Connection-safe failures** — a validation error becomes a `HubException` carrying the structured field errors, which SignalR sends back to the caller without tearing down the connection.
- **DI helper** — `AddOrionGuardSignalR()` registers the filter and scans your assemblies for validators.

## Install

```bash
dotnet add package OrionGuard.SignalR
```

Requires `Microsoft.AspNetCore.SignalR` in your application. The core `OrionGuard` package is brought in transitively.

## Quick start

```csharp
using Moongazing.OrionGuard.SignalR;

builder.Services.AddOrionGuardSignalR();

builder.Services.AddSignalR(options =>
{
    options.AddFilter<OrionGuardHubFilter>();
});

// Any IValidator<T> registered in DI is applied to hub method arguments:
public sealed class ChatHub : Hub
{
    public Task Send(ChatMessage message)
    {
        // message is already validated here.
        return Clients.All.SendAsync("receive", message);
    }
}
```

## Targets

.NET 8.0, .NET 9.0, .NET 10.0.

## License

MIT. See the [main repository](https://github.com/tunahanaliozturk/OrionGuard) for full docs, CHANGELOG, and samples.
