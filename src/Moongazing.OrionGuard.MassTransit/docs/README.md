# OrionGuard.MassTransit

MassTransit integration for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). A consume filter validates every consumed message with its registered validators, so an invalid message faults before it reaches the consumer and ends up in the endpoint's `_error` queue.

## Install

```bash
dotnet add package OrionGuard.MassTransit
```

Use MassTransit 8.x, the last Apache-2.0 licensed line. The package is built against MassTransit 8.5.10; MassTransit 9 and later are commercially licensed and not supported. The core `OrionGuard` package is installed as a dependency. You choose the transport package yourself (`MassTransit.RabbitMQ`, `MassTransit.Azure.ServiceBus.Core`, and so on).

## Quick start

Register your validators in DI, then add the filter in the transport callback:

```csharp
using MassTransit;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.MassTransit;

builder.Services.AddOrionGuard();
builder.Services.AddValidator<SubmitOrder, SubmitOrderValidator>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<SubmitOrderConsumer>();

    x.UsingRabbitMq((context, cfg) => // from MassTransit.RabbitMQ
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
```

A validator is an ordinary OrionGuard validator for the message type:

```csharp
public sealed record SubmitOrder(string OrderId, string CustomerEmail);

public sealed class SubmitOrderValidator : AbstractValidator<SubmitOrder>
{
    public SubmitOrderValidator()
    {
        RuleFor(x => x.OrderId, "OrderId", p => p.NotEmpty());
        RuleFor(x => x.CustomerEmail, "CustomerEmail", p => p.NotEmpty().Email());
    }
}
```

`SubmitOrderConsumer.Consume` now only receives messages that passed validation.

## What this package adds

- `OrionGuardConsumeFilter<TMessage>`: a MassTransit `IFilter<ConsumeContext<TMessage>>`. It runs every `IValidator<TMessage>` registered for the consumed message type, one after another. If any of them reports an error, it throws `MessageValidationException` and the consumer does not run.
- `MessageValidationException`: carries every `ValidationError` from every validator in `Errors`, plus the consumed `MessageType`. Its `ToString()` appends one line per error.
- `UseOrionGuardValidation(IRegistrationContext)` on `IConsumePipeConfigurator`: adds the filter as a scoped consume filter. Call it on the bus configurator to validate on every receive endpoint, or on one receive endpoint configurator to validate only there.

Message types with no registered validator pass through unchanged.

## What happens to an invalid message

The filter throws inside the consume pipe, so MassTransit handles the exception like any other consume fault:

1. A `UseMessageRetry` policy wraps the filter. Without `r.Ignore<MessageValidationException>()`, every retry validates the message again and fails the same way.
2. When the message is not retried again, MassTransit moves it to the `<queue>_error` queue. The `MT-Fault-ExceptionType` header is `Moongazing.OrionGuard.MassTransit.MessageValidationException`, and `MT-Fault-Message` names the message type and the error count.
3. MassTransit publishes a `ReceiveFault` event. It does not publish `Fault<TMessage>`, because no consumer ran.

The same `r.Ignore<MessageValidationException>()` call works in `UseDelayedRedelivery`, which takes the same retry configurator.

## One endpoint only

```csharp
x.UsingRabbitMq((context, cfg) =>
{
    cfg.ReceiveEndpoint("submit-order", e =>
    {
        e.UseOrionGuardValidation(context);
        e.ConfigureConsumer<SubmitOrderConsumer>(context);
    });
});
```

## Validation details

- The filter is a MassTransit scoped filter. MassTransit creates it from the consume scope, the same scope the consumer is resolved from, so scoped validators and validators with scoped dependencies (a `DbContext`, for example) share the consumer's instances and are disposed with the scope.
- It calls `ValidateAsync` with `context.CancellationToken`, so both synchronous rules and `RuleForAsync` rules run.
- Validators are resolved for the consumed type, the `T` of `IConsumer<T>`, not for the runtime type of the message. MassTransit deserializes an interface contract into a generated class, so a validator registered as `IValidator<IOrderSubmitted>` still applies to a consumer of `IOrderSubmitted`.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- `MassTransit` `[8.5.10,9.0.0)`. MassTransit 9 moved to a commercial license, so this package stays on the Apache-2.0 8.x line, and the upper bound makes an application that pulls in MassTransit 9 fail restore instead of silently running against an unsupported major.

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard) (validators and `AddValidator`), [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR) (the same validation for in-process requests), [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry) (validation metrics and traces)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
