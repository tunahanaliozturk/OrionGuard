# OrionGuard.MediatR

MediatR 12 integration for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). It adds pipeline behaviors that run every `IValidator<TRequest>` before the request or stream handler, and a domain-event dispatcher that publishes OrionGuard domain events as MediatR notifications.

## Install

```bash
dotnet add package OrionGuard.MediatR
```

Use MediatR 12.x, the last Apache-2.0 licensed line. The package is built against MediatR 12.4.1; MediatR 13 and later are not supported. The core `OrionGuard` package is installed as a dependency.

## Quick start

```csharp
using MediatR;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.MediatR;

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddOrionGuardMediatR(typeof(Program).Assembly);

public sealed record CreateUserCommand(string Email, string Password) : IRequest<Guid>;

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email, nameof(CreateUserCommand.Email), p => p.NotEmpty().Email());
        RuleFor(x => x.Password, nameof(CreateUserCommand.Password), p => p.Length(8, 128));
    }
}
```

## Validation behavior

- `AddOrionGuardMediatR(params Assembly[] assemblies)` registers `ValidationBehavior<,>` as an open-generic transient `IPipelineBehavior<,>` and `StreamValidationBehavior<,>` as an open-generic transient `IStreamPipelineBehavior<,>`. It also registers every non-abstract `IValidator<T>` implementation found in the given assemblies as transient. It does not call `AddMediatR`, so register MediatR yourself.
- `ValidationBehavior<TRequest, TResponse>` resolves all `IValidator<TRequest>` services and runs their `ValidateAsync` one after another, so both synchronous rules and `RuleForAsync` rules run, and validators can share a scoped `DbContext`. The results are combined into one.
- `StreamValidationBehavior<TRequest, TResponse>` does the same for `IStreamRequest<TResponse>` requests sent with `IMediator.CreateStream`. Validation runs when the stream is first enumerated, before the handler yields its first item.
- If any error is found, it throws `AggregateValidationException` (namespace `Moongazing.OrionGuard.Core`) with every error in `Errors`, and the handler is not called. Requests without a validator pass straight through.
- In ASP.NET Core, the exception handler in [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) turns `AggregateValidationException` into a `ValidationProblemDetails` response with status 422 by default.

## Domain events through MediatR

```csharp
using MediatR;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.MediatR.DomainEvents;

builder.Services.AddOrionGuardDomainEvents();
builder.Services.AddOrionGuardMediatRDomainEvents();

// Opt in per event: implement INotification alongside the OrionGuard base record.
public sealed record OrderPlaced(Guid OrderId) : DomainEventBase, INotification;

public sealed class SendOrderConfirmation : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

- `AddOrionGuardMediatRDomainEvents()` removes any existing `IDomainEventDispatcher` registration and registers `MediatRDomainEventDispatcher` as scoped. Every dispatch through `IDomainEventDispatcher` is then published with MediatR's `IPublisher.Publish`. This includes the dispatch done by the OrionGuard.EntityFrameworkCore save-changes interceptor.
- Each event must implement `INotification`. Otherwise `DispatchAsync` throws `InvalidOperationException`.
- A batch of events is published one at a time, in order.
- With OpenTelemetry, the order must be `AddOrionGuardDomainEvents()`, then `AddOrionGuardMediatRDomainEvents()`, then `WithOpenTelemetryDomainEvents()`. Calling the MediatR bridge after the OpenTelemetry decorator throws `InvalidOperationException`, because the bridge would otherwise remove the decorator.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- `MediatR` 12.x (built against 12.4.1)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard.AspNetCore](https://www.nuget.org/packages/OrionGuard.AspNetCore) (maps validation exceptions to HTTP responses), [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry) (validator and domain-event telemetry), [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (dispatches domain events on `SaveChanges`)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
