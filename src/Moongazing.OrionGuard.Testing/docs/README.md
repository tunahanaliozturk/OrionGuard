# OrionGuard.Testing

Two things that are awkward to test by hand: what an aggregate *raised* rather than what it stored, and what a validator actually enforces. Capture domain events and assert on them, or pin a validator's rules to a snapshot file that fails when they drift.

```bash
dotnet add package OrionGuard.Testing
```

```csharp
namespace Shop.Tests;

using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Testing.DomainEvents;
using Xunit;

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
public sealed record OrderShipped(OrderId OrderId) : DomainEventBase;
public sealed record OrderCancelled(OrderId OrderId) : DomainEventBase;

public sealed class Order(OrderId id) : AggregateRoot<OrderId>(id)
{
    public void Ship() => RaiseEvent(new OrderShipped(Id));
    public void Cancel() => RaiseEvent(new OrderCancelled(Id));
}

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

A failed assertion throws `DomainEventAssertionException` (or `ValidatorAssertionException` on the validator side), which every runner reports as a failure — the package has no dependency on xUnit, NUnit, MSTest, FluentAssertions, Verify or Snapshooter, so it does not pick your test stack for you. The message names what it wanted and lists what it found: `Expected OrderShipped to be raised but it was not. Captured events: [OrderCancelled]`.

## DomainEventCapture

Namespace `Moongazing.OrionGuard.Testing.DomainEvents`. A snapshot of events to assert on.

| Member | Description |
| --- | --- |
| `static DomainEventCapture From(IAggregateRoot aggregate)` | Pulls the aggregate's pending events — this **empties its buffer**. |
| `static DomainEventCapture FromList(IEnumerable<IDomainEvent> events)` | Wraps a list you already have. |
| `IReadOnlyList<IDomainEvent> All` | Everything captured, in the order raised. |
| `TEvent Single<TEvent>()` | The only event of that type; throws `InvalidOperationException` for none or several. |
| `IEnumerable<TEvent> OfType<TEvent>()` | All events of that type. |
| `bool Contains<TEvent>()` / `Contains<TEvent>(predicate)` | Whether one was captured, optionally matching a predicate. |
| `DomainEventAssertions Should()` | Starts a fluent chain. |

`Single<TEvent>()` is the hand-off point when you want your own assertions on the payload:

```csharp
namespace Shop.Tests.Detail;

using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Testing.DomainEvents;
using Xunit;

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
public sealed record OrderShipped(OrderId OrderId) : DomainEventBase;

public sealed class Order(OrderId id) : AggregateRoot<OrderId>(id)
{
    public void Ship() => RaiseEvent(new OrderShipped(Id));
}

public class ShippedPayloadTests
{
    [Fact]
    public void Ship_carries_the_order_id()
    {
        var order = new Order(new OrderId(Guid.NewGuid()));
        order.Ship();

        OrderShipped shipped = DomainEventCapture.From(order).Single<OrderShipped>();

        Assert.Equal(order.Id, shipped.OrderId);
    }
}
```

## Assertions

Every `DomainEventAssertions` method returns the same object, so calls chain, and each throws `DomainEventAssertionException` on failure.

| Method | Passes when |
| --- | --- |
| `HaveRaised<TEvent>(Func<TEvent, bool>? predicate = null)` | At least one `TEvent` was captured, and matches `predicate` if one was given. |
| `NotHaveRaised<TEvent>()` | No `TEvent` was captured. |
| `HaveRaisedExactly(int expected).Of<TEvent>()` | Exactly `expected` events of `TEvent` were captured. |

## InMemoryDomainEventDispatcher

An `IDomainEventDispatcher` that records instead of invoking handlers — for testing code that publishes through the dispatcher rather than exposing the aggregate.

| Member | Description |
| --- | --- |
| `DispatchAsync(IDomainEvent, CancellationToken)` | Records one event; no handler runs. |
| `DispatchAsync(IEnumerable<IDomainEvent>, CancellationToken)` | Records them in iteration order; no handler runs. |
| `IReadOnlyList<IDomainEvent> Captured` | Everything dispatched so far, in order. |
| `DomainEventAssertions Should()` | Fluent assertions over `Captured`. |
| `void Clear()` | Forgets everything captured. |

```csharp
namespace Shop.Tests.Handlers;

using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Testing.DomainEvents;
using Xunit;

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
public sealed record OrderShipped(OrderId OrderId) : DomainEventBase;

public sealed class Order(OrderId id) : AggregateRoot<OrderId>(id)
{
    public void Ship() => RaiseEvent(new OrderShipped(Id));
}

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

In an integration test that builds the real container (`WebApplicationFactory`, say), replace the registration:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Testing.DomainEvents;

public static class TestServices
{
    public static void UseInMemoryDispatcher(IServiceCollection services, InMemoryDomainEventDispatcher dispatcher) =>
        services.Replace(ServiceDescriptor.Singleton<IDomainEventDispatcher>(dispatcher));
}
```

`AddOrionGuardDomainEvents()` registers its dispatcher with `TryAdd`, so registering the in-memory one *before* that call works just as well.

## Pin a validator's rules to a snapshot

Namespace `Moongazing.OrionGuard.Testing.Validators`. A snapshot records what a validator enforces and fails when that changes without the file being updated — on a validator several people edit, the diff on the snapshot says in one screen what the change did to the contract.

```csharp
namespace Shop.Tests.Validators;

using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Testing.Validators;
using Xunit;

public sealed class CreateUserRequest
{
    public string? Email { get; set; }
    public int Age { get; set; }
    public string? Notes { get; set; }
}

public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email, nameof(CreateUserRequest.Email), p => p.NotEmpty().Email());
        RuleFor(x => x.Age > 0, "Age must be greater than 0.", nameof(CreateUserRequest.Age));
    }
}

public class CreateUserValidatorTests
{
    [Fact]
    public Task Rules_are_unchanged() =>
        OrionGuardSnapshot.Of<CreateUserValidator, CreateUserRequest>().MatchAsync();
}
```

The first run writes `CreateUserValidator.verified.txt` next to the test's source file and passes; review it, commit it, and after that any change to what the validator reports fails the test. The file is what the validator says about a fixed set of probe values, one section per property:

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

Probe values follow the property's type: `null` for anything nullable, then `""` / `"   "` / `"x"` / a 256-character string, `-1` / `0` / `1` for numbers (`0` / `1` / `2` when unsigned), `false` / `true`, every member of an enum, the empty and a non-empty `Guid`, `DateTime.MinValue` and a fixed past and future date, and `[]` / `[one]` for collections. A collection is probed with a value of its *declared* type — an array, a `List<T>`, or the type itself for a `HashSet<T>` or `Dictionary<TKey, TValue>` — so a rule that accepts `null` but rejects an empty collection is seen.

| Member | Description |
| --- | --- |
| `static ValidatorSnapshot<TValidator, TModel> OrionGuardSnapshot.Of<TValidator, TModel>()` | Starts a snapshot. Both types need a public parameterless constructor, and `TValidator` must implement `IValidator<TModel>`. |
| `Task MatchAsync(string? snapshotPath = null, bool ci = false, CancellationToken cancellationToken = default)` | Compares against the snapshot and throws `ValidatorAssertionException` on a mismatch. The token reaches the rules, so an async rule doing I/O is cancelled with the test. |
| `string Render()` | The snapshot text, without touching the file system. |

A mismatch writes a `.received.txt` beside the snapshot and names the first three differing lines:

```text
CreateUserValidator no longer matches its snapshot '...\CreateUserValidator.verified.txt'.
  line 5:
    snapshot:   -1 -> AGE: Age must be positive.
    actual:     -1 -> AGE: Age must be greater than 0.
```

`snapshotPath` moves the file; by default it is `{TValidator}.verified.txt` in the directory of the source file that called `MatchAsync`. `ci` decides what a *missing* snapshot means: `false` (the default) writes it and passes, `true` fails, so a build server never silently accepts a snapshot nobody reviewed. No environment variable is read for you — pass the switch:

```csharp
namespace Shop.Tests.Validators.Ci;

using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Testing.Validators;

public sealed class CreateUserRequest
{
    public string? Email { get; set; }
}

public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator() =>
        RuleFor(x => x.Email, nameof(CreateUserRequest.Email), p => p.NotEmpty().Email());
}

public static class SnapshotOnCi
{
    public static Task MatchAsync() =>
        OrionGuardSnapshot.Of<CreateUserValidator, CreateUserRequest>()
            .MatchAsync(ci: Environment.GetEnvironmentVariable("CI") is not null);
}
```

Add `*.received.txt` to your `.gitignore`.

## Fail the build when a property has no rule

`OrionGuardCoverage` reports the properties of a model that carry no rule at all — the test that fails when someone adds a field and forgets to validate it.

```csharp
namespace Shop.Tests.Coverage;

using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Testing.Validators;
using Xunit;

public sealed class CreateUserRequest
{
    public string? Email { get; set; }
    public string? Notes { get; set; }
}

public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator() =>
        RuleFor(x => x.Email, nameof(CreateUserRequest.Email), p => p.NotEmpty().Email());
}

public class CreateUserCoverageTests
{
    [Fact]
    public void Every_property_is_validated() =>
        OrionGuardCoverage.For<CreateUserValidator, CreateUserRequest>()
            .AssertEveryPropertyIsValidated(except: [nameof(CreateUserRequest.Notes)]);
}
```

| Member | Description |
| --- | --- |
| `static ValidatorCoverage<TValidator, TModel> OrionGuardCoverage.For<TValidator, TModel>()` | Measures coverage. Throws `ValidatorAssertionException` when no rule can be enumerated at all, rather than calling zero coverage a pass. |
| `IReadOnlyList<string> UnvalidatedProperties` | The properties no rule was found for, in ordinal order. |
| `void AssertEveryPropertyIsValidated(params string[] except)` | Throws unless every property outside `except` carries a rule. Each `except` entry must be a real property, so a rename cannot leave a stale exemption behind. |

A rule is found either statically, from a `Moongazing.OrionGuard.Attributes.ValidationAttribute` on the property, or behaviourally, by running the validator over the probe values and reading `ParameterName` off every error. The behavioural route is the only one available for `AbstractValidator<T>` and `FluentStyleValidator<T>`: both hold their rules as closures over an opaque predicate and expose neither the rule list nor the property a rule targets, so there is nothing to reflect over.

Both sweeps go through `ValidateAsync`, so `RuleFor` and `RuleForAsync` rules count equally and each rule runs exactly once per probe value.

## What this does not do

- **`DomainEventCapture.From(aggregate)` is destructive.** It calls `PullDomainEvents()`, so the aggregate's buffer is empty afterwards. Capture once, after the action under test — a second capture sees nothing, and production code that pulls after your test did will find nothing either.
- **The in-memory dispatcher runs no handlers.** It proves an event was published, never that a handler did the right thing. Test handlers directly.
- **There are three event assertions.** `HaveRaised`, `NotHaveRaised`, `HaveRaisedExactly(n).Of<T>()` — no ordering assertions, no "exactly these events and no others", no payload matchers beyond a predicate. Reach for `All` or `Single<T>()` and your own test framework for anything else.
- **Coverage cannot see a rule no probe value can break.** `Must(x => x.Age != 42)` never fails for any probed value, so its property is reported as unvalidated. The same goes for a rule on a collection property whose declared type nothing can be constructed for (`ReadOnlyCollection<T>` has no parameterless constructor, `ImmutableArray<T>` no usable default): the property is probed with `null` only, the snapshot says `(no value of this type could be constructed to probe with)`, and any rule that does not fail for `null` is invisible.
- **A rule that throws is not coverage.** A probe that makes the validator throw is recorded as `-> threw NullReferenceException` — the exception type only, never its message. The exception is an async rule inside `AbstractValidator<T>`, which catches it and reports `RULE_EXECUTION_FAILED` with the exception's own text embedded; that text is localized on some runtimes and is the one place a snapshot can differ between machines. It is a bug in the rule either way.
- **A snapshot is only as stable as your messages.** The text comes from your validator, so a validator that localizes its messages produces a snapshot that depends on the test's culture. Everything the package controls is pinned: ordinal ordering, invariant-culture numbers and dates, `\n` endings on every platform.
- **A rule reporting a name that is not a property** is listed under that name — in the snapshot's final `[unattributed]` section — not against a property. A rule that reports several properties at once (`"StartDate,EndDate"`) is listed under each of them.
- **It is not a test framework adapter.** A failure is an exception, so a runner reports it as an error rather than as a pretty diff.

## Targets

`net8.0`, `net9.0`, `net10.0`. Depends only on the core `OrionGuard` package.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) (aggregates, events, dispatcher) · [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (dispatch on `SaveChanges`) · [OrionGuard.MediatR](https://www.nuget.org/packages/OrionGuard.MediatR)

## Documentation

- [OrionGuard README](https://github.com/tunahanaliozturk/OrionGuard#readme)
- [CHANGELOG](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
