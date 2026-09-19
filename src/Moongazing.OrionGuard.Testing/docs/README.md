# OrionGuard.Testing

Test helpers for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). For domain events: capture what an aggregate raised, swap in an in-memory dispatcher, and assert on the result with a small fluent API. For validators: pin what a validator enforces to a snapshot file, and fail a test when a model grows a property nobody validates. It has no dependency on xUnit, NUnit, MSTest, FluentAssertions, Verify, or Snapshooter; a failed assertion throws `DomainEventAssertionException` or `ValidatorAssertionException`, which every test runner reports as a failure.

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

## Validator snapshots

Namespace `Moongazing.OrionGuard.Testing.Validators`. A snapshot test records the rules a validator actually enforces in a `.verified.txt` file, and fails when they change without the file being updated. Useful on a validator that several people edit: the diff on the snapshot says, in one screen, what the change did to the contract.

```csharp
using Moongazing.OrionGuard.Testing.Validators;

public class CreateUserValidatorTests
{
    [Fact]
    public Task Rules_are_unchanged()
        => OrionGuardSnapshot.Of<CreateUserValidator, CreateUserRequest>().MatchAsync();
}
```

The first run writes `CreateUserValidator.verified.txt` next to the test's source file and passes; review it and commit it. After that, any change to what the validator reports fails the test.

The snapshot is what the validator says about a fixed set of probe values, one section per property:

```text
validator: CreateUserValidator
model: CreateUserRequest

[Age]
  -1 -> AGE: Age must be greater than 0.
  0 -> AGE: Age must be greater than 0.
  1 -> accepted

[Email]
  null -> EMAIL: Email is required.
  "" -> EMAIL: Email is required.
  "   " -> EMAIL: Email is required.
  "x" -> EMAIL: Email must be a valid address.
  string(256) -> EMAIL: Email must be a valid address.

[Notes]
  null -> accepted
  "" -> accepted
  "   " -> accepted
  "x" -> accepted
  string(256) -> accepted
```

Probe values depend on the property's type: `null` for anything nullable, then `""` / `"   "` / `"x"` / a 256-character string, `-1` / `0` / `1` for numbers (`0` / `1` / `2` for unsigned ones), `false` / `true`, every member of an enum, the empty and a non-empty `Guid`, `DateTime.MinValue` and a fixed past and future date, and `[]` / `[default]` for collections. A rule that reports several properties at once (`"StartDate,EndDate"`) is listed under each of them; a rule whose reported name is not a property of the model is listed under a final `[unattributed]` section. A probe that makes the validator throw is recorded as `-> threw NullReferenceException` — only the exception type, never its message.

Nothing machine- or run-dependent goes into the file: properties and errors are ordered ordinally, numbers and dates are formatted with the invariant culture, and lines end with `\n` on every platform. The messages come from your validator, so a validator that localizes its messages produces a snapshot that depends on the test's culture.

| Member | Description |
| --- | --- |
| `static ValidatorSnapshot<TValidator, TModel> OrionGuardSnapshot.Of<TValidator, TModel>()` | Starts a snapshot. Both types need a public parameterless constructor, and `TValidator` must implement `IValidator<TModel>`. |
| `Task MatchAsync(string? snapshotPath = null, bool ci = false)` | Compares against the snapshot and throws `ValidatorAssertionException` on a mismatch. |
| `string Render()` | The snapshot text, without touching the file system. |

`MatchAsync` writes a `.received.txt` beside the snapshot whenever the two differ, and the failure message names the first three differing lines:

```text
CreateUserValidator no longer matches its snapshot '...\CreateUserValidator.verified.txt'.
  line 5:
    snapshot:   -1 -> AGE: Age must be positive.
    actual:     -1 -> AGE: Age must be greater than 0.
```

`snapshotPath` overrides where the snapshot lives; by default it is `{TValidator}.verified.txt` in the directory of the source file that called `MatchAsync`. `ci` decides what a *missing* snapshot means: with the default `false` it is written and the test passes, with `true` it is a failure, so a build server never silently accepts a snapshot nobody reviewed. No environment variable is read for you — pass the switch yourself:

```csharp
await OrionGuardSnapshot.Of<CreateUserValidator, CreateUserRequest>()
    .MatchAsync(ci: Environment.GetEnvironmentVariable("CI") is not null);
```

Add `*.received.txt` to your `.gitignore`.

## Rule coverage

`OrionGuardCoverage` reports which properties of a model carry no rule at all, so a test fails when someone adds a property and forgets to validate it.

```csharp
[Fact]
public void Every_property_is_validated()
    => OrionGuardCoverage.For<CreateUserValidator, CreateUserRequest>()
        .AssertEveryPropertyIsValidated(except: [nameof(CreateUserRequest.Notes)]);
```

| Member | Description |
| --- | --- |
| `static ValidatorCoverage<TValidator, TModel> OrionGuardCoverage.For<TValidator, TModel>()` | Measures coverage. Throws `ValidatorAssertionException` when the validator's rules cannot be enumerated at all. |
| `IReadOnlyList<string> UnvalidatedProperties` | The properties no rule was found for, in ordinal order. |
| `void AssertEveryPropertyIsValidated(params string[] except)` | Throws unless every property outside `except` carries a rule. Each `except` entry must be a real property, so a rename cannot leave a stale exemption behind. |

**What it can see.** A rule is found in one of two ways. Statically, from a `Moongazing.OrionGuard.Attributes.ValidationAttribute` on the property — the only rule shape this package can read without running anything. Behaviourally, by running the validator over the probe values above and reading the `ParameterName` off every error it reports. Behavioural discovery is the only option for `AbstractValidator<T>` and `FluentStyleValidator<T>`: both hold their rules as closures over an opaque predicate and expose neither the rule list nor the property a rule targets, so there is nothing to reflect over.

**What it cannot see.**

- A rule no probe value can make fail — `Must(x => x.Age != 42)` — is invisible, and its property is reported as unvalidated.
- A rule that reports a name which is not a property of the model is attributed to that name, not to a property.
- A rule that throws instead of reporting does not count as coverage.
- A property carrying a validation attribute counts as validated even when `TValidator` never runs the attribute validator.

**When nothing at all is visible**, `For<TValidator, TModel>()` throws rather than reporting every property as unvalidated, because a validator with no rules and one whose rules never fail for a probe value look identical from the outside:

```text
NeverFailingValidator reported nothing for any probe value of CreateUserRequest, so its rules could not
be enumerated. ...
```

## Targets

- `net8.0`, `net9.0`, `net10.0`
- Depends only on the core `OrionGuard` package. No test-framework dependency.

## Documentation

- [OrionGuard README](https://github.com/tunahanaliozturk/OrionGuard#readme)
- [CHANGELOG](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard) (aggregates, domain events, dispatcher), [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (dispatch on `SaveChanges`, transactional outbox), [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR) (MediatR-backed dispatcher)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
