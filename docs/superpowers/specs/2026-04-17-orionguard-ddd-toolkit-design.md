# OrionGuard DDD Toolkit — Design Spec

**Date:** 2026-04-17
**Author:** Tunahan Ali Ozturk
**Status:** Design approved, pending implementation plan
**Target versions:** v6.1.0, v6.2.0, v6.3.0

---

## 1. Motivation

OrionGuard is a comprehensive guard clause and validation ecosystem for .NET. Guard clauses are fundamentally about **invariant enforcement** — which is also the heart of DDD tactical patterns (value objects, entities, aggregate roots, business rules). Extending OrionGuard with a DDD toolkit is a natural fit: the same fail-fast / fluent / localized philosophy carries over directly to domain modeling.

Goals:

- Provide first-class DDD tactical primitives (ValueObject, Entity, AggregateRoot, StronglyTypedId) that integrate with existing OrionGuard guards.
- Provide a lightweight domain-event mechanism with a MediatR bridge and EF Core dispatch interceptor.
- Provide business-rule enforcement (`IBusinessRule`) that participates in OrionGuard's dual throw/accumulate semantics and 14-language localization.
- Ship without introducing any new NuGet package — only extend the three existing packages (core, Generators, MediatR).

Non-goals (explicitly deferred):

- Specification pattern (`ISpecification<T>`) — second-phase feature.
- `Result<T>` / `Maybe<T>` DDD-flavored types — second-phase feature.
- SmartEnum — deferred; evaluate demand after v6.3.
- Repository / UnitOfWork abstractions — deferred.
- MassTransit / Wolverine bridges — YAGNI; revisit on demand.
- Dapper / MongoDB / Newtonsoft serializers for StronglyTypedId — YAGNI.

---

## 2. Architecture

### 2.1 Package placement

| Package | Role in DDD toolkit |
|---------|----------------------|
| `Moongazing.OrionGuard` (core) | All DDD primitives, business-rule abstractions, domain-event abstractions, exceptions, localization keys, `Guard.Against.BrokenRule` / `Guard.Against.DefaultStronglyTypedId` extensions. |
| `Moongazing.OrionGuard.Generators` | `[StronglyTypedId<T>]` attribute + source generator. |
| `Moongazing.OrionGuard.MediatR` | `MediatRDomainEventDispatcher`, `DispatchDomainEventsInterceptor` (EF Core), `AddOrionGuardDomainEvents()` DI extension. |
| `Moongazing.OrionGuard.AspNetCore` | `BusinessRuleValidationException` → RFC 9457 ProblemDetails mapping. |

**No new NuGet packages are introduced.**

### 2.2 Namespace layout (core package)

```
Moongazing.OrionGuard.Domain
├── Primitives
│   ├── IValueObject                (marker)
│   ├── ValueObject                 (abstract base class)
│   ├── Entity<TId>                 (abstract)
│   ├── IAggregateRoot              (non-generic marker)
│   ├── AggregateRoot<TId>          (abstract)
│   └── StronglyTypedId<TValue>     (abstract record)
├── Events
│   ├── IDomainEvent
│   ├── IDomainEventDispatcher
│   └── DomainEventBase             (abstract record)
├── Rules
│   ├── IBusinessRule
│   ├── IAsyncBusinessRule
│   ├── BusinessRule                (abstract)
│   └── AsyncBusinessRule           (abstract)
└── Exceptions
    ├── BusinessRuleValidationException
    └── DomainInvariantException
```

Extensions added to existing folders:

- `Extensions/BusinessRuleGuards.cs` — `Guard.Against.BrokenRule(...)`, `Guard.Against.BrokenRuleAsync(...)`, `Guard.Against.DefaultStronglyTypedId(...)`.
- `Extensions/DomainValidationExtensions.cs` — `Validate.Rule(...)`, `Validate.Rules(...)`.

### 2.3 Dependency rules

- `Moongazing.OrionGuard.Domain` MUST have zero external dependencies beyond what the core package already uses.
- MediatR/EF Core integration lives in `Moongazing.OrionGuard.MediatR` (already references both).
- Source generator lives in `Moongazing.OrionGuard.Generators` (already Roslyn-based).
- The core DDD toolkit MUST be usable without referencing MediatR, EF Core, or the Generators package.

---

## 3. Domain Primitives (Milestone A — v6.1.0)

### 3.1 ValueObject (hybrid style)

Two interchangeable styles are supported.

**Style 1 — Abstract base class** (for behavior-rich VOs):

```csharp
public abstract class ValueObject : IValueObject, IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(ValueObject? left, ValueObject? right);
    public static bool operator !=(ValueObject? left, ValueObject? right);
}
```

Equality is component-wise using `HashCode.Combine` over the `GetEqualityComponents()` enumerable.

**Style 2 — Marker interface** (for record-based pure-data VOs):

```csharp
public interface IValueObject { }

public sealed record Address(string Street, string City, string PostalCode) : IValueObject
{
    public Address
    {
        Ensure.That(Street).NotNullOrWhiteSpace();
        Ensure.That(City).NotNullOrWhiteSpace();
        Ensure.That(PostalCode).NotNullOrWhiteSpace().Length(4, 10);
    }
}
```

The `IValueObject` interface is purely a marker — C# records provide the equality semantics automatically.

### 3.2 Entity<TId>

```csharp
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    protected Entity(TId id);
    protected Entity(); // EF Core constructor

    public bool Equals(Entity<TId>? other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right);
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right);

    protected static void CheckRule(IBusinessRule rule);
    protected static Task CheckRuleAsync(IAsyncBusinessRule rule, CancellationToken ct = default);
}
```

- Identity equality: two entities with the same `Id` are equal regardless of other state.
- `CheckRule` / `CheckRuleAsync` are `protected` — only the entity itself can enforce its invariants, preserving encapsulation.
- Parameterless constructor exists for EF Core / serialization.

### 3.3 AggregateRoot<TId>

```csharp
public interface IAggregateRoot
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    IReadOnlyCollection<IDomainEvent> PullDomainEvents();
}

public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : notnull
{
    public IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    protected AggregateRoot(TId id);
    protected AggregateRoot();

    protected void RaiseEvent(IDomainEvent @event);
    public IReadOnlyCollection<IDomainEvent> PullDomainEvents();
}
```

- `IAggregateRoot` is a non-generic marker so the EF Core interceptor can discover aggregates without knowing `TId`.
- `PullDomainEvents()` returns the current events **and clears** the internal list — prevents double-dispatch if `SaveChanges` runs twice.
- `RaiseEvent` uses `Ensure.That(@event).NotNull()` to reject null events.

### 3.4 StronglyTypedId

**Base record (manual style):**

```csharp
public abstract record StronglyTypedId<TValue>(TValue Value)
    where TValue : notnull, IEquatable<TValue>;

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static OrderId New() => new(Guid.NewGuid());
    public static OrderId Empty => new(Guid.Empty);
}
```

**Source generator (automatic style):**

```csharp
[StronglyTypedId<Guid>]
public readonly partial struct CustomerId;
```

Supported value types: `Guid`, `int`, `long`, `string`, `Ulid` (net9.0+ TFM conditional).

Generated members (per target type):

- `IEquatable<T>`, `IComparable<T>`, `GetHashCode`, `ToString`
- `static New()` (for Guid/Ulid), `static Empty`
- EF Core `ValueConverter<T, TValue>`
- `System.Text.Json.JsonConverter<T>`
- `TypeConverter` (for ASP.NET Core route/query/form binding)

**DI extension** (Generators-package-side):

```csharp
services.AddOrionGuardStronglyTypedIds(); // registers all generated EF Core converters via assembly scan
```

### 3.5 Guard extensions for StronglyTypedId

```csharp
// In Moongazing.OrionGuard/Extensions/BusinessRuleGuards.cs
Guard.Against.DefaultStronglyTypedId(orderId, nameof(orderId));
// Throws if: orderId is null, or its Value equals default(TValue)
// (Guid.Empty, 0, "", default(Ulid))
```

### 3.6 Localization keys added

| Key | English default |
|-----|------------------|
| `DefaultStronglyTypedId` | `"{0} must not be the default value."` |
| `DomainInvariantViolated` | `"Domain invariant violated: {0}."` |
| `BusinessRuleBroken` | `"Business rule broken: {0}."` |

All 14 languages (EN, TR, DE, FR, ES, PT, AR, JA, IT, ZH, KO, RU, NL, PL) receive translations.

---

## 4. Domain Events (Milestone C — v6.2.0)

### 4.1 Abstractions (core package)

```csharp
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
}

public abstract record DomainEventBase : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default);
}
```

### 4.2 MediatR bridge (MediatR package)

```csharp
internal sealed class MediatRDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IPublisher _publisher;
    public MediatRDomainEventDispatcher(IPublisher publisher);
    public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct);
}
```

For each event, calls `IPublisher.Publish(event, ct)`. Handlers are standard MediatR `INotificationHandler<TEvent>` implementations.

### 4.3 EF Core interceptor (MediatR package)

```csharp
public sealed class DispatchDomainEventsInterceptor : SaveChangesInterceptor
{
    public DispatchDomainEventsInterceptor(IDomainEventDispatcher dispatcher);

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct);
}
```

- Runs **after** `SaveChanges` completes successfully — ensures persistence before event side effects.
- Scans `ChangeTracker.Entries<IAggregateRoot>()`.
- Calls `PullDomainEvents()` (clears internal list).
- Dispatches via `IDomainEventDispatcher`.

### 4.4 DI extension (MediatR package)

```csharp
public static IServiceCollection AddOrionGuardDomainEvents(this IServiceCollection services)
{
    services.TryAddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
    services.AddScoped<DispatchDomainEventsInterceptor>();
    return services;
}
```

User registers the interceptor on their `DbContextOptionsBuilder`:

```csharp
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(connStr)
           .AddInterceptors(sp.GetRequiredService<DispatchDomainEventsInterceptor>()));
```

### 4.5 Ordering and failure semantics

- Events are dispatched in the order they were raised.
- If dispatch throws, the EF Core `SaveChanges` transaction has already committed — user is responsible for outbox-pattern if transactional guarantee is needed. The README section for v6.2.0 and the XML docs on `DispatchDomainEventsInterceptor` MUST call this out explicitly, with a sample outbox-pattern reference.
- Double-dispatch is prevented because `PullDomainEvents()` clears the list.

---

## 5. Business Rules (Milestone E — v6.3.0)

### 5.1 Abstractions

```csharp
public interface IBusinessRule
{
    bool IsBroken();
    string MessageKey { get; }
    string DefaultMessage { get; }
    object[]? MessageArgs => null;
}

public interface IAsyncBusinessRule
{
    Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default);
    string MessageKey { get; }
    string DefaultMessage { get; }
    object[]? MessageArgs => null;
}

public abstract class BusinessRule : IBusinessRule
{
    public abstract bool IsBroken();
    public abstract string MessageKey { get; }
    public abstract string DefaultMessage { get; }
    public virtual object[]? MessageArgs => null;
    public string GetLocalizedMessage();
}

public abstract class AsyncBusinessRule : IAsyncBusinessRule { /* symmetric */ }
```

### 5.2 Exception

```csharp
public sealed class BusinessRuleValidationException : Exception
{
    public string MessageKey { get; }
    public string? RuleName { get; }
    public BusinessRuleValidationException(IBusinessRule rule);
}
```

Constructor resolves the message via `ValidationMessages.TryGet(rule.MessageKey, rule.MessageArgs) ?? rule.DefaultMessage`.

### 5.3 Three usage surfaces

OrionGuard's dual throw/accumulate philosophy is preserved:

```csharp
// 1. Throw (Guard.Against paradigm)
Guard.Against.BrokenRule(new OrderMustBePaidRule(order));
await Guard.Against.BrokenRuleAsync(new InventoryAvailableRule(order, repo), ct);

// 2. Accumulate (GuardResult paradigm)
GuardResult result = Validate.Rule(new OrderMustBePaidRule(order)).ToResult();
GuardResult combined = Validate.Rules(
    new OrderMustBePaidRule(order),
    new OrderMustHaveShippingAddressRule(order)
).ToResult();

// 3. Entity-encapsulated (DDD paradigm)
public void Ship()
{
    CheckRule(new OrderMustBePaidRule(this));
    CheckRule(new OrderMustHaveShippingAddressRule(this));
    RaiseEvent(new OrderShippedEvent(Id));
}
```

### 5.4 ASP.NET Core integration

`Moongazing.OrionGuard.AspNetCore` gains a `BusinessRuleValidationExceptionHandler` (implements `IExceptionHandler`) that maps `BusinessRuleValidationException` to a 422 ProblemDetails body:

```json
{
  "type": "https://orionguard.dev/errors/business-rule-broken",
  "title": "Business rule violation",
  "status": 422,
  "detail": "Order must be paid before shipping.",
  "extensions": {
    "ruleName": "OrderMustBePaidRule",
    "messageKey": "OrderMustBePaidRule"
  }
}
```

Registered by existing `AddOrionGuardAspNetCore()` extension.

---

## 6. Localization

14 languages × 3 new keys = 42 new translations added to existing resource files in `Moongazing.OrionGuard/Localization/`.

User-defined `BusinessRule` types register their own keys:

```csharp
ValidationMessages.Register("OrderMustBePaidRule", new Dictionary<string, string>
{
    ["en"] = "Order must be paid before shipping.",
    ["tr"] = "Sipariş kargolanmadan önce ödenmiş olmalıdır.",
    ["de"] = "Die Bestellung muss vor dem Versand bezahlt sein.",
    // ... other languages
});
```

The `Register` API is new in v6.1.0; existing `ValidationMessages.SetCulture` / `Get` remain unchanged.

---

## 7. Testing strategy

### 7.1 Unit tests (`tests/Moongazing.OrionGuard.Tests/Domain/`)

| File | Coverage |
|------|----------|
| `Primitives/ValueObjectTests.cs` | Component equality, hash code stability, null safety, operator overloads, record-based equality via `IValueObject` |
| `Primitives/EntityTests.cs` | Identity equality, null id rejection, protected ctor accessibility via subclass |
| `Primitives/AggregateRootTests.cs` | Event raising, `PullDomainEvents` clears list, order preservation, null event rejection, `IAggregateRoot` marker |
| `Primitives/StronglyTypedIdTests.cs` | Base record equality, `New()`, `Empty` semantics, `Guard.Against.DefaultStronglyTypedId` |
| `Rules/BusinessRuleTests.cs` | Throw path, result path, localization resolution, async rules |
| `Events/DomainEventTests.cs` | Metadata defaults, deterministic ordering |

### 7.2 Integration tests (`tests/Moongazing.OrionGuard.MediatR.Tests/Domain/`)

- `MediatRDomainEventDispatcherTests.cs` — publish fan-out, cancellation
- `DispatchDomainEventsInterceptorTests.cs` — uses in-memory SQLite + real `DbContext` + mock MediatR publisher; asserts events fire after `SavedChangesAsync`, list is cleared, ordering preserved, no dispatch on `SaveChanges` failure

### 7.3 Source generator tests (`tests/Moongazing.OrionGuard.Generators.Tests/`)

- `StronglyTypedIdGeneratorTests.cs` — snapshot tests for each supported value type (`Guid`, `int`, `long`, `string`, `Ulid`), compile verification, EF Core converter generation, JSON converter generation

### 7.4 Benchmarks (`benchmarks/Moongazing.OrionGuard.Benchmarks/`)

- `DomainPrimitivesBenchmark.cs` — ValueObject equality vs record equality vs Ardalis.GuardClauses baseline; StronglyTypedId allocation profile; AggregateRoot event raising overhead

---

## 8. Roadmap and milestones

| Version | Scope | Est. source files | Est. test/bench files | Est. test count |
|---------|-------|-------------------|-----------------------|-----------------|
| v6.1.0 | Domain primitives — ValueObject, Entity, AggregateRoot (+ IAggregateRoot), StronglyTypedId (base + source gen), `Guard.Against.DefaultStronglyTypedId`, localization keys, `ValidationMessages.Register` | ~15 | 6 (4 unit + 1 generator snapshot + 1 benchmark) | ~60 |
| v6.2.0 | Domain events — `IDomainEvent`, `DomainEventBase`, `IDomainEventDispatcher`, MediatR bridge, EF Core interceptor, `AddOrionGuardDomainEvents` | ~8 | 3 (1 unit + 2 integration) | ~30 |
| v6.3.0 | Business rules — `IBusinessRule`, `BusinessRule`, async variants, `BusinessRuleValidationException`, `Guard.Against.BrokenRule`, `Validate.Rule` / `Validate.Rules`, AspNetCore ProblemDetails handler | ~10 | 2 (1 unit + 1 aspnetcore handler) | ~40 |

**Totals:** ~33 source files, ~11 test/benchmark files, ~130 tests, 3 releases.

Each release ships:

- CHANGELOG update
- README section update
- FEATURES.md + FEATURES-v6.md update
- NuGet package release notes
- Sample/demo snippet in `demo/` directory

---

## 9. Open questions

None at design time — all design decisions have been made with the user:

- Hybrid ValueObject style (base class + marker interface): **approved**
- StronglyTypedId approach (base record + source generator): **approved**
- Domain event bridge (MediatR + EF Core interceptor): **approved**
- Business rule semantics (throw + accumulate + entity-encapsulated): **approved**
- Package placement (no new NuGet packages): **approved**
- StronglyTypedId value types (Guid, int, long, string, Ulid): **approved**
- Three-milestone delivery (v6.1 → v6.2 → v6.3): **approved**

---

## 10. Appendix: Usage example end-to-end

```csharp
// Value object (record style)
public sealed record Money(decimal Amount, Currency Currency) : IValueObject
{
    public Money
    {
        Ensure.That(Amount).GreaterThanOrEqualTo(0);
        Ensure.That(Currency).NotNull();
    }

    public Money Add(Money other)
    {
        Guard.Against.BrokenRule(new CurrenciesMustMatchRule(this, other));
        return this with { Amount = Amount + other.Amount };
    }
}

// Strongly-typed id (source-generated)
[StronglyTypedId<Guid>]
public readonly partial struct OrderId;

// Business rule
public sealed class OrderMustBePaidRule(Order order) : BusinessRule
{
    public override bool IsBroken() => order.Status != OrderStatus.Paid;
    public override string MessageKey => nameof(OrderMustBePaidRule);
    public override string DefaultMessage => "Order must be paid before shipping.";
}

// Domain event
public sealed record OrderShippedEvent(OrderId OrderId) : DomainEventBase;

// Aggregate root
public sealed class Order : AggregateRoot<OrderId>
{
    public OrderStatus Status { get; private set; }
    public Money Total { get; private set; } = default!;

    private Order() { }

    public Order(OrderId id, Money total) : base(id)
    {
        Guard.Against.DefaultStronglyTypedId(id);
        Ensure.That(total).NotNull();
        Total = total;
        Status = OrderStatus.Pending;
    }

    public void Ship()
    {
        CheckRule(new OrderMustBePaidRule(this));
        Status = OrderStatus.Shipped;
        RaiseEvent(new OrderShippedEvent(Id));
    }
}

// Application wiring
builder.Services.AddOrionGuard();
builder.Services.AddOrionGuardAspNetCore();
builder.Services.AddOrionGuardMediatR(typeof(Program).Assembly);
builder.Services.AddOrionGuardDomainEvents();
builder.Services.AddOrionGuardStronglyTypedIds();
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseSqlServer(connStr)
           .AddInterceptors(sp.GetRequiredService<DispatchDomainEventsInterceptor>()));
```

This exercises every primitive, every integration point, and every milestone.
