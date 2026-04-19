# OrionGuard v6.1 — DDD Domain Primitives

**First phase of a three-phase DDD toolkit rollout. Brings tactical Domain-Driven Design primitives into the OrionGuard ecosystem without breaking or renaming anything in v6.0.**

.NET 8 | .NET 9 | .NET 10 -- MIT License

---

## Overview

v6.1 adds `Moongazing.OrionGuard.Domain` — a suite of DDD tactical primitives built on top of the existing guard-clause foundation. The same fail-fast / fluent / 14-language-localized philosophy carries over directly from validation to domain modeling.

**Phased delivery:**

- **v6.1 (this release):** `ValueObject`, `Entity<TId>`, `AggregateRoot<TId>`, `StronglyTypedId<TValue>`, source generator, guard extension, DI helper, localization keys, abstractions for events + rules.
- **v6.2:** Domain event dispatcher (`IDomainEventDispatcher`), MediatR bridge, EF Core `SaveChanges` interceptor.
- **v6.3:** Full `BusinessRule` base class, `Guard.Against.BrokenRule`, `Validate.Rule` / `Validate.Rules`, ASP.NET Core `BusinessRuleValidationException` → RFC 9457 ProblemDetails mapping.

---

## Domain Primitives

### ValueObject (hybrid style)

Two interchangeable styles are supported:

**Abstract base class** — for behavior-rich value objects:

```csharp
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Ensure.That(amount).GreaterThanOrEqualTo(0);
        Amount = amount; Currency = currency;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount; yield return Currency;
    }
}
```

**Marker interface** — for record-based pure-data VOs (records give structural equality for free):

```csharp
public sealed record Address(string Street, string City, string PostalCode) : IValueObject;
```

### Entity<TId>

Identity-based equality — two entities with the same `Id` are equal regardless of other state.

```csharp
public sealed class Customer : Entity<int>
{
    public Customer(int id) : base(id) { }
    public string Email { get; private set; } = string.Empty;

    public void ChangeEmail(string newEmail)
    {
        Ensure.That(newEmail).NotNull().Email();
        Email = newEmail;
    }
}
```

The protected `CheckRule(IBusinessRule)` and `CheckRuleAsync(IAsyncBusinessRule, CancellationToken)` helpers throw `BusinessRuleValidationException` when a rule is violated — encapsulation is preserved because only the entity itself can enforce its invariants.

### AggregateRoot<TId>

Extends `Entity<TId>`. Adds a domain-event buffer consumed by dispatchers:

```csharp
public sealed class Order : AggregateRoot<OrderId>
{
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;

    public Order(OrderId id) : base(id)
    {
        id.AgainstDefaultStronglyTypedId(nameof(id));
    }

    public void Ship()
    {
        CheckRule(new OrderMustBePaidRule(this));
        Status = OrderStatus.Shipped;
        RaiseEvent(new OrderShippedEvent(Id));
    }
}
```

The non-generic marker `IAggregateRoot` allows dispatchers (e.g., v6.2's EF Core interceptor) to discover aggregates from `ChangeTracker.Entries()` without knowing the identifier type.

---

## StronglyTypedId — Two Flavors

### Manual base record

```csharp
public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
public sealed record CustomerId(int Value) : StronglyTypedId<int>(Value);
```

Constraint `where TValue : notnull, IEquatable<TValue>` ensures the underlying type supports value equality. Derived type identity prevents `OrderId(g)` from equaling `CustomerId(g)` even with the same `Value`.

### Source generator — `[StronglyTypedId<TValue>]`

```csharp
[StronglyTypedId<Guid>]
public readonly partial struct OrderId;
```

The OrionGuard generator emits **four companion sources** per decorated type:

| Companion | Generated type | Purpose |
|-----------|---------------|---------|
| Partial body | `OrderId` (completed) | `IEquatable<T>`, operators, `GetHashCode`, `ToString`, `Value` property, ctor, `New()`/`Empty` |
| EF Core converter | `OrderIdEfCoreValueConverter` | `ValueConverter<OrderId, Guid>` for database mapping |
| JSON converter | `OrderIdJsonConverter` | `System.Text.Json.Serialization.JsonConverter<OrderId>` for API payloads |
| Type converter | `OrderIdTypeConverter` | `TypeConverter` for ASP.NET Core route/query/form binding |

**Supported underlying types:** `System.Guid`, `int`, `long`, `string`, `System.Ulid` (net9.0+).

No reflection at runtime. No external package reference needed in the generator — it emits fully-qualified type names in generated strings.

---

## Guard Extension

```csharp
var orderId = new OrderId(Guid.NewGuid());
orderId.AgainstDefaultStronglyTypedId(nameof(orderId));
// Throws NullValueException if orderId is null
// Throws ZeroValueException if orderId.Value == Guid.Empty / 0 / ""
```

Implemented via constrained generics — no reflection. `where TValue : notnull, IEquatable<TValue>` on the receiver type `StronglyTypedId<TValue>`.

---

## Dependency Injection

```csharp
using Moongazing.OrionGuard.DependencyInjection;

// Program.cs
builder.Services.AddOrionGuardStronglyTypedIds();
// ↑ Scans the calling assembly for all source-generated *EfCoreValueConverter types
//   and registers each as a singleton. Idempotent across multiple calls.

// Override with explicit assemblies:
builder.Services.AddOrionGuardStronglyTypedIds(typeof(Order).Assembly, typeof(Customer).Assembly);
```

---

## Localization

Three new keys added to all 14 bundled languages (42 new translations):

| Key | Use |
|-----|-----|
| `DefaultStronglyTypedId` | Thrown by `AgainstDefaultStronglyTypedId` when wrapped value is default |
| `BusinessRuleBroken` | Used by `BusinessRuleValidationException` when no `MessageKey` is registered (v6.3) |
| `DomainInvariantViolated` | Used by `DomainInvariantException` for raw invariant violations |

Existing `ValidationMessages.SetCulture` / `SetCultureForCurrentScope` / `AddMessages` API works unchanged.

---

## Abstractions Shipped in v6.1 for Later Phases

To keep `Entity.CheckRule` and `AggregateRoot.RaiseEvent` immediately usable, the interface contracts for domain events and business rules ship in v6.1, even though their full implementations arrive later:

| Type | Phase | Status in v6.1 |
|------|-------|----------------|
| `IDomainEvent` | v6.1 | Interface (`EventId`, `OccurredOnUtc`) |
| `IDomainEventDispatcher` | v6.2 | Not yet |
| `DomainEventBase` record | v6.2 | Not yet |
| `IBusinessRule`, `IAsyncBusinessRule` | v6.1 | Interfaces |
| `BusinessRule` / `AsyncBusinessRule` bases | v6.3 | Not yet |
| `BusinessRuleValidationException` | v6.1 | Full — resolves via `ValidationMessages` |
| `DomainInvariantException` | v6.1 | Full |

---

## Performance

`DomainPrimitivesBenchmark` (BenchmarkDotNet, net8.0 + net9.0) measures:

- `ValueObject` abstract-class equality vs record equality
- `AggregateRoot.RaiseEvent` + `PullDomainEvents` round-trip

Run from repo root:

```bash
dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks -- DomainPrimitivesBenchmark
```

---

## End-to-End Example

```csharp
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Domain.Rules;
using Moongazing.OrionGuard.Extensions;
using Moongazing.OrionGuard.DependencyInjection;

// Strongly-typed id via source generator
[StronglyTypedId<Guid>]
public readonly partial struct OrderId;

// Value object as a record
public sealed record Money(decimal Amount, string Currency) : IValueObject;

// Domain event
public sealed record OrderShippedEvent(OrderId Id, DateTime OccurredOnUtc) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

// Business rule
public sealed class OrderMustBePaidRule(Order order) : IBusinessRule
{
    public bool IsBroken() => order.Status != OrderStatus.Paid;
    public string MessageKey => nameof(OrderMustBePaidRule);
    public string DefaultMessage => "Order must be paid before shipping.";
}

// Aggregate root
public sealed class Order : AggregateRoot<OrderId>
{
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public Money Total { get; private set; } = default!;

    public Order(OrderId id, Money total) : base(id)
    {
        id.AgainstDefaultStronglyTypedId(nameof(id));
        Ensure.That(total).NotNull();
        Total = total;
    }

    public void Pay() => Status = OrderStatus.Paid;

    public void Ship()
    {
        CheckRule(new OrderMustBePaidRule(this));
        Status = OrderStatus.Shipped;
        RaiseEvent(new OrderShippedEvent(Id, DateTime.UtcNow));
    }
}

// Application wiring
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOrionGuard();
builder.Services.AddOrionGuardAspNetCore();
builder.Services.AddOrionGuardStronglyTypedIds();
```
