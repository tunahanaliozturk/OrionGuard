# OrionGuard.Testing

Test helpers for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard) domain events: capture what an aggregate raised, swap in an in-memory dispatcher, and assert on the result with a small fluent API. It has no dependency on xUnit, NUnit, MSTest, or FluentAssertions; a failed assertion throws `DomainEventAssertionException`, which every test runner reports as a failure.

## Install

```bash
dotnet add package OrionGuard.Testing
```

## Quick start

The examples use this aggregate, built on the core `OrionGuard` primitives:

```csharp
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
public sealed record OrderShipped(OrderId OrderId) : DomainEventBase;
public sealed record OrderCancelled(OrderId OrderId) : DomainEventBase;

public sealed class Order(OrderId id) : AggregateRoot<OrderId>(id)
{
    public void Ship() => RaiseEvent(new OrderShipped(Id));
    public void Cancel() => RaiseEvent(new OrderCancelled(Id));
}
```

A unit test with xUnit (any other runner works the same way):

```csharp
using Moongazing.OrionGuard.Testing.DomainEvents;
using Xunit;

public class OrderTests
{
    [Fact]
    public void Ship_raises_OrderShipped()
    {
        var order = new Order(new OrderId(Guid.NewGuid()));

        order.Ship();

        DomainEventCapture events = DomainEventCapture.From(order);
        events.Should()
            .HaveRaised<OrderShipped>(e => e.OrderId == order.Id)
            .NotHaveRaised<OrderCancelled>()
            .HaveRaisedExactly(1).Of<OrderShipped>();
    }
}
```

`DomainEventCapture.From(order)` calls `order.PullDomainEvents()`, so it empties the aggregate's event buffer. Capture after the action under test, and only once per action.

## DomainEventCapture

Namespace `Moongazing.OrionGuard.Testing.DomainEvents`. A snapshot of domain events to assert on.

| Member | Description |
| --- | --- |
| `static DomainEventCapture From(IAggregateRoot aggregate)` | Pulls the aggregate's pending events (empties its buffer). |
| `static DomainEventCapture FromList(IEnumerable<IDomainEvent> events)` | Wraps an existing list of events without pulling from anything. |
| `IReadOnlyList<IDomainEvent> All` | Every captured event, in the order it was raised. |
| `TEvent Single<TEvent>()` | The only event of that type. Throws `InvalidOperationException` when there are none or more than one. |
| `IEnumerable<TEvent> OfType<TEvent>()` | All captured events of that type. |
| `bool Contains<TEvent>()` | True when at least one event of that type was captured. |
| `bool Contains<TEvent>(Func<TEvent, bool> predicate)` | True when at least one event of that type matches the predicate. |
| `DomainEventAssertions Should()` | Starts a fluent assertion chain. |

`Single<TEvent>()` is handy when you want to inspect one event's data with your own assertions:

```csharp
OrderShipped shipped = DomainEventCapture.From(order).Single<OrderShipped>();
Assert.Equal(order.Id, shipped.OrderId);
```

## Assertions

`DomainEventAssertions` methods return the same assertions object, so calls chain. Each one throws `DomainEventAssertionException` when it fails.

| Method | Passes when |
| --- | --- |
| `HaveRaised<TEvent>(Func<TEvent, bool>? predicate = null)` | At least one event of `TEvent` was captured (and matches `predicate`, if given). |
| `NotHaveRaised<TEvent>()` | No event of `TEvent` was captured. |
| `HaveRaisedExactly(int expected).Of<TEvent>()` | Exactly `expected` events of `TEvent` were captured. |

Failure messages from `HaveRaised` and `NotHaveRaised` list the type names of every captured event, for example `Expected OrderShipped to be raised but it was not. Captured events: [OrderCancelled]`.

## InMemoryDomainEventDispatcher

An `IDomainEventDispatcher` that records every event it is given instead of invoking handlers. Use it where production code publishes events through the dispatcher.

| Member | Description |
| --- | --- |
| `Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)` | Records one event. No handlers run. |
| `Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)` | Records the events in iteration order. No handlers run. |
| `IReadOnlyList<IDomainEvent> Captured` | Everything dispatched so far, in order. |
| `DomainEventAssertions Should()` | Fluent assertions over `Captured`. |
| `void Clear()` | Forgets all captured events. |

```csharp
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Testing.DomainEvents;
using Xunit;

// Application code under test.
public sealed class ShipOrderHandler(IDomainEventDispatcher dispatcher)
{
    public async Task HandleAsync(Order order, CancellationToken cancellationToken)
    {
        order.Ship();
        await dispatcher.DispatchAsync(order.PullDomainEvents(), cancellationToken);
    }
}

public class ShipOrderHandlerTests
{
    [Fact]
    public async Task Handle_publishes_OrderShipped()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        var handler = new ShipOrderHandler(dispatcher);

        await handler.HandleAsync(new Order(new OrderId(Guid.NewGuid())), CancellationToken.None);

        dispatcher.Should().HaveRaisedExactly(1).Of<OrderShipped>();
    }
}
```

In an integration test that builds the real service container (for example with `WebApplicationFactory`), replace the registered dispatcher:

```csharp
services.Replace(ServiceDescriptor.Singleton<IDomainEventDispatcher>(dispatcher));
```

`Replace` comes from `Microsoft.Extensions.DependencyInjection.Extensions`. `AddOrionGuardDomainEvents()` registers its dispatcher with `TryAdd`, so registering the in-memory dispatcher before calling it also works.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- Depends only on the core `OrionGuard` package. No test-framework dependency.

## Documentation

- [OrionGuard README](https://github.com/tunahanaliozturk/OrionGuard#readme)
- [CHANGELOG](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard) (aggregates, domain events, dispatcher), [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (dispatch on `SaveChanges`, transactional outbox), [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR) (MediatR-backed dispatcher)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
