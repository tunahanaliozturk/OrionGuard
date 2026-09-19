# OrionGuard.MassTransit

Validates a message before your consumer sees it, so a malformed message faults in the pipe and lands in the endpoint's `_error` queue instead of half-processing.

```bash
dotnet add package OrionGuard.MassTransit
```

```csharp
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.MassTransit;

public sealed record SubmitOrder(string OrderId, string CustomerEmail);

public sealed class SubmitOrderValidator : AbstractValidator<SubmitOrder>
{
    public SubmitOrderValidator()
    {
        RuleFor(x => x.OrderId, "OrderId", p => p.NotEmpty());
        RuleFor(x => x.CustomerEmail, "CustomerEmail", p => p.NotEmpty().Email());
    }
}

public sealed class SubmitOrderConsumer : IConsumer<SubmitOrder>
{
    public Task Consume(ConsumeContext<SubmitOrder> context) => Task.CompletedTask;
}

public static class BusSetup
{
    public static void Add(IServiceCollection services)
    {
        services.AddOrionGuard();
        services.AddValidator<SubmitOrder, SubmitOrderValidator>();

        services.AddMassTransit(x =>
        {
            x.AddConsumer<SubmitOrderConsumer>();

            x.UsingInMemory((context, cfg) => // or UsingRabbitMq, UsingAzureServiceBus, ...
            {
                cfg.UseMessageRetry(r =>
                {
                    r.Interval(3, TimeSpan.FromSeconds(5));
                    // A validation failure is deterministic, so retrying it cannot succeed.
                    r.Ignore<MessageValidationException>();
                });

                cfg.UseOrionGuardValidation(context);
                cfg.ConfigureEndpoints(context);
            });
        });
    }
}
```

`SubmitOrderConsumer.Consume` now only receives messages that passed validation. An invalid one throws `MessageValidationException`, which carries every `ValidationError` from every validator in `Errors` plus the consumed `MessageType`; its `ToString()` appends one line per error. The core `OrionGuard` package comes along as a dependency; the transport package (`MassTransit.RabbitMQ`, `MassTransit.Azure.ServiceBus.Core`, ...) is your choice.

## Where to put the filter

`UseOrionGuardValidation(IRegistrationContext)` is an extension on `IConsumePipeConfigurator`. On the bus configurator it covers every receive endpoint; on one endpoint configurator it covers only that endpoint:

```csharp
using MassTransit;
using Moongazing.OrionGuard.MassTransit;

public sealed record SubmitOrder(string OrderId);

public sealed class SubmitOrderConsumer : IConsumer<SubmitOrder>
{
    public Task Consume(ConsumeContext<SubmitOrder> context) => Task.CompletedTask;
}

public static class OneEndpointOnly
{
    public static void Configure(IBusRegistrationContext context, IBusFactoryConfigurator cfg) =>
        cfg.ReceiveEndpoint("submit-order", e =>
        {
            e.UseOrionGuardValidation(context);
            e.ConfigureConsumer<SubmitOrderConsumer>(context);
        });
}
```

## What happens to an invalid message

The filter throws inside the consume pipe, so MassTransit treats it as any other consume fault:

1. A `UseMessageRetry` policy wraps the filter. Without `r.Ignore<MessageValidationException>()` every retry re-validates and fails identically — the same call works in `UseDelayedRedelivery`, which takes the same configurator.
2. When no retry is left, MassTransit moves the message to `<queue>_error`. `MT-Fault-ExceptionType` is `Moongazing.OrionGuard.MassTransit.MessageValidationException`, and `MT-Fault-Message` names the message type and the error count.
3. MassTransit publishes `ReceiveFault`. It does not publish `Fault<TMessage>`, because no consumer ran.

## How validators are resolved

`OrionGuardConsumeFilter<TMessage>` is a MassTransit scoped filter, created from the consume scope — the same scope the consumer comes from — so scoped validators and validators holding a scoped `DbContext` share the consumer's instances and are disposed with it. Every `IValidator<TMessage>` registered for the type runs, one after another, through `ValidateAsync` with `context.CancellationToken`.

Validators are resolved for the *consumed* type, the `T` in `IConsumer<T>`, not for the runtime type of the deserialized object. That is what makes interface contracts work: MassTransit materializes `IOrderSubmitted` into a generated class, and a validator registered as `IValidator<IOrderSubmitted>` still applies.

## What this does not do

- **It does not validate what you publish or send.** This is a consume filter only; an invalid message is caught at the receiving end, after it has already been through the broker.
- **A message type with no registered validator passes through**, silently. There is no assembly scanning here — register each validator with `AddValidator<T, TValidator>()`.
- **It cannot tell the sender what was wrong.** The failure is a fault, not a reply; the errors reach the `_error` queue and your fault observers, not the publisher.
- **Retries are not ignored for you.** Without `r.Ignore<MessageValidationException>()` a validation failure burns the whole retry budget before it dead-letters.
- **MassTransit 8.x only.** MassTransit 9 moved to a commercial licence, so this package stays on the Apache-2.0 line and its package reference carries an upper bound below 9.0.0 — a solution that pulls MassTransit 9 fails restore rather than silently running on an unsupported major.

## Targets

`net8.0`, `net9.0`, `net10.0`; MassTransit 8.x.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) (validators and `AddValidator`) · [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR) (the same validation for in-process requests) · [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (publish events through a transactional outbox) · [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
