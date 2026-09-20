# OrionGuard.MediatR

Validates a MediatR request before its handler runs, so a command handler can assume its input is good — plus a bridge that publishes OrionGuard domain events as MediatR notifications.

```bash
dotnet add package OrionGuard.MediatR
```

```csharp
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.MediatR;

public sealed record CreateUserCommand(string Email, string Password) : IRequest<Guid>;

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email, nameof(CreateUserCommand.Email), p => p.NotEmpty().Email());
        RuleFor(x => x.Password, nameof(CreateUserCommand.Password), p => p.Length(8, 128));
    }
}

public static class MediatRSetup
{
    public static void Add(IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateUserCommand>());
        services.AddOrionGuardMediatR(typeof(CreateUserCommand).Assembly);
    }
}
```

`mediator.Send(new CreateUserCommand("nope", "short"))` now throws `AggregateValidationException` (namespace `Moongazing.OrionGuard.Core`) with every error in `Errors`, and the handler never runs. In an ASP.NET Core host, [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) turns that exception into a `ValidationProblemDetails` response. The core `OrionGuard` package comes along as a dependency.

## Registration

`AddOrionGuardMediatR(params Assembly[] assemblies)`:

- registers `ValidationBehavior<,>` as an open-generic transient `IPipelineBehavior<,>`,
- registers `StreamValidationBehavior<,>` as an open-generic transient `IStreamPipelineBehavior<,>`,
- registers every non-abstract `IValidator<T>` implementation it finds in `assemblies` as transient.

It does not call `AddMediatR` — register MediatR yourself, in either order.

## What the behaviors do

`ValidationBehavior<TRequest, TResponse>` resolves *all* `IValidator<TRequest>` services and runs their `ValidateAsync` one after another, never concurrently, so `RuleForAsync` rules run and two validators may share the same scoped `DbContext`. Their results are combined into one.

`StreamValidationBehavior<TRequest, TResponse>` does the same for an `IStreamRequest<TResponse>` sent with `IMediator.CreateStream`. The check runs when the stream is first enumerated, before the handler yields its first item.

A request type with no registered validator passes straight through.

## Domain events through MediatR

```csharp
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.MediatR.DomainEvents;

// Opt in per event: implement INotification alongside the OrionGuard base record.
public sealed record OrderPlaced(Guid OrderId) : DomainEventBase, INotification;

public sealed class SendOrderConfirmation : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class DomainEventSetup
{
    public static void Add(IServiceCollection services)
    {
        services.AddOrionGuardDomainEvents();
        services.AddOrionGuardMediatRDomainEvents();
    }
}
```

`AddOrionGuardMediatRDomainEvents()` removes any existing `IDomainEventDispatcher` registration and registers `MediatRDomainEventDispatcher` as scoped, so every dispatch — including the one the [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) `SaveChanges` interceptor performs — goes out through MediatR's `IPublisher.Publish`, one event at a time, in order.

With [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry), call in this order: `AddOrionGuardDomainEvents()`, `AddOrionGuardMediatRDomainEvents()`, then `WithOpenTelemetryDomainEvents()`. The bridge throws `InvalidOperationException` if it runs after the telemetry decorator, because replacing the registration would silently drop the decorator.

## What this does not do

- **It does not validate notifications.** `IPublisher.Publish` does not go through `IPipelineBehavior<,>`; only `Send` and `CreateStream` are covered.
- **Every event you dispatch through the MediatR bridge must implement `INotification`.** `DispatchAsync` throws `InvalidOperationException` for one that does not — the opt-in is per event record, not global.
- **Assembly scanning finds `IValidator<T>` implementations only** in the assemblies you pass, and registers them as transient. Nothing looks at the whole `AppDomain`, and a validator you forget to hand over means the request silently passes.
- **A validation failure is an exception, not a result.** Outside ASP.NET Core (a worker, a console host) `AggregateValidationException` is yours to catch.
- **MediatR 12.x only** — the last Apache-2.0 licensed line. MediatR 13 and later need a commercial licence key and are not supported here.

## Targets

`net8.0`, `net9.0`, `net10.0`; MediatR 12.x.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) · [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) (maps the exception to HTTP) · [OrionGuard.MassTransit](https://www.nuget.org/packages/OrionGuard.MassTransit) (the same idea for messages off a bus) · [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) · [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
