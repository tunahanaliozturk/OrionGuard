# OrionGuard DDD Toolkit v6.1.0 — Domain Primitives Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship v6.1.0 of OrionGuard with a DDD tactical-primitives foundation: `ValueObject` (hybrid style), `Entity<TId>`, `AggregateRoot<TId>`, `StronglyTypedId<TValue>` (base record + source generator with EF Core / System.Text.Json / TypeConverter), and the supporting guard extensions, exceptions, and localization keys.

**Architecture:** Add a new `Moongazing.OrionGuard.Domain` namespace to the core package with sub-namespaces `Primitives`, `Events`, `Rules`, `Exceptions`. Ship minimal-but-stable interfaces for `IDomainEvent`, `IBusinessRule`, and `IAsyncBusinessRule` in v6.1.0 so that `AggregateRoot.RaiseEvent` and `Entity.CheckRule` are usable immediately — the full business-rule base class, domain event dispatcher, and integration helpers arrive in v6.2.0 and v6.3.0 respectively. Add a new `[StronglyTypedId<TValue>]` incremental source generator to the `Moongazing.OrionGuard.Generators` project that emits the partial type body plus EF Core `ValueConverter`, `System.Text.Json.JsonConverter<T>`, and `TypeConverter` companion types.

**Tech Stack:** .NET 8 / 9 / 10 multi-targeting (core), netstandard2.0 (generator), xUnit (tests), BenchmarkDotNet (benchmarks). Uses existing `Ensure.That()`, `ValidationMessages` infrastructure.

**Spec Reference:** [`docs/superpowers/specs/2026-04-17-orionguard-ddd-toolkit-design.md`](../specs/2026-04-17-orionguard-ddd-toolkit-design.md), sections 2, 3, 6, 7, 8 (v6.1.0 row).

**Scope adjustment vs spec:** `ValidationMessages.Register()` removed from scope — the existing `ValidationMessages.AddMessages(cultureName, dictionary)` public API already performs identical thread-safe registration. Documented in Task 21.

---

## File Structure

**Core package — `src/Moongazing.OrionGuard/` (new files):**

```
Domain/
├── Primitives/
│   ├── IValueObject.cs                [marker interface]
│   ├── ValueObject.cs                 [abstract base class with component equality]
│   ├── Entity.cs                      [Entity<TId> abstract]
│   ├── IAggregateRoot.cs              [non-generic marker]
│   ├── AggregateRoot.cs               [AggregateRoot<TId> abstract]
│   └── StronglyTypedId.cs             [abstract record]
├── Events/
│   └── IDomainEvent.cs                [interface only — DomainEventBase in v6.2]
├── Rules/
│   ├── IBusinessRule.cs
│   └── IAsyncBusinessRule.cs
└── Exceptions/
    ├── BusinessRuleValidationException.cs
    └── DomainInvariantException.cs

Extensions/
└── StronglyTypedIdGuards.cs           [Guard.Against.DefaultStronglyTypedId]

Localization/
└── ValidationMessages.cs              [MODIFY — add 3 new keys × 14 languages = 42 entries]
```

**Generators package — `src/Moongazing.OrionGuard.Generators/` (new files):**

```
StronglyTypedIds/
├── StronglyTypedIdAttribute.cs        [attribute source emitted into compilation]
├── StronglyTypedIdGenerator.cs        [incremental generator entry point]
├── StronglyTypedIdEmitter.cs          [partial type body emission]
├── EfCoreConverterEmitter.cs          [EF Core ValueConverter emission]
├── JsonConverterEmitter.cs            [System.Text.Json JsonConverter emission]
├── TypeConverterEmitter.cs            [TypeConverter emission]
└── SupportedValueType.cs              [enum + mapping: Guid/int/long/string/Ulid]

DependencyInjection/
└── StronglyTypedIdServiceExtensions.cs [AddOrionGuardStronglyTypedIds — scans assemblies for generated converters]
```

**Test project — `tests/Moongazing.OrionGuard.Tests/` (new files, flat layout to match repo convention):**

```
ValueObjectTests.cs
EntityTests.cs
AggregateRootTests.cs
StronglyTypedIdTests.cs
StronglyTypedIdGuardTests.cs
```

**Generator test project — new project `tests/Moongazing.OrionGuard.Generators.Tests/`:**

```
Moongazing.OrionGuard.Generators.Tests.csproj
StronglyTypedIdGeneratorTests.cs       [snapshot tests with Verify.Xunit]
Snapshots/                              [generated on first run, committed]
```

**Benchmarks — `benchmarks/Moongazing.OrionGuard.Benchmarks/` (new file):**

```
DomainPrimitivesBenchmark.cs           [VO equality + AggregateRoot.RaiseEvent + StronglyTypedId]
```

**Docs — `docs/` (modified):**

```
docs/FEATURES-v6.md                    [MODIFY — add v6.1.0 section]
CHANGELOG.md                           [MODIFY — v6.1.0 entry]
README.md                              [MODIFY — add "DDD Primitives" section]
src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj [MODIFY — version 6.1.0, PackageReleaseNotes]
src/Moongazing.OrionGuard.Generators/Moongazing.OrionGuard.Generators.csproj [MODIFY — version 6.1.0]
```

---

## Conventions (apply to every task)

- **Target framework:** new types in core use `#nullable enable` (already project-wide), target multi-frameworks.
- **Ulid support is net9.0+ only:** guard with `#if NET9_0_OR_GREATER` where `System.Ulid` is referenced.
- **Test naming:** `<Method>_Should<ExpectedOutcome>_When<Condition>` matching existing `GuardTests.cs` style.
- **xUnit using:** `Xunit` is a global using in the test csproj; do not add `using Xunit;`.
- **Build command:** from repository root `OrionGuard/`, run `dotnet build src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj` etc. with explicit csproj paths.
- **Test command:** `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~<Class>.<Method>`.
- **Commit working directory:** run git commands from `OrionGuard/` (the directory containing the .sln).
- **Commit message format:** Conventional Commits — `feat(domain): ...`, `test(domain): ...`, `docs: ...`.

---

## Task 1: Scaffold `Domain/` folder and `IValueObject` marker

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Primitives/IValueObject.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/ValueObjectTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Moongazing.OrionGuard.Tests/ValueObjectTests.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class ValueObjectTests
{
    private sealed record RecordAddress(string Street, string City) : IValueObject;

    [Fact]
    public void IValueObject_ShouldBeImplementableByRecord_WhenMarkerAppliedToRecord()
    {
        IValueObject vo = new RecordAddress("Main St", "Ankara");

        Assert.NotNull(vo);
        Assert.IsAssignableFrom<IValueObject>(vo);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~ValueObjectTests`
Expected: FAIL — `error CS0246: The type or namespace name 'IValueObject' could not be found`.

- [ ] **Step 3: Create `IValueObject.cs`**

Create `src/Moongazing.OrionGuard/Domain/Primitives/IValueObject.cs`:

```csharp
namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Marker interface for value objects in the Domain-Driven Design sense.
/// <para>
/// Apply to <see langword="record"/> types to get structural equality for free, or inherit from
/// <see cref="ValueObject"/> for behavior-rich value objects with explicit equality components.
/// </para>
/// </summary>
public interface IValueObject
{
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~ValueObjectTests`
Expected: PASS — 1 test passed.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Primitives/IValueObject.cs \
        tests/Moongazing.OrionGuard.Tests/ValueObjectTests.cs
git commit -m "feat(domain): add IValueObject marker interface for record-based value objects"
```

---

## Task 2: `ValueObject` abstract base class with component equality

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Primitives/ValueObject.cs`
- Modify: `tests/Moongazing.OrionGuard.Tests/ValueObjectTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `tests/Moongazing.OrionGuard.Tests/ValueObjectTests.cs` (inside the class, above the closing brace):

```csharp
    private sealed class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Equals_ShouldReturnTrue_WhenComponentsMatch()
    {
        var a = new Money(100m, "TRY");
        var b = new Money(100m, "TRY");

        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenAnyComponentDiffers()
    {
        var a = new Money(100m, "TRY");
        var b = new Money(100m, "USD");

        Assert.False(a.Equals(b));
        Assert.True(a != b);
    }

    [Fact]
    public void GetHashCode_ShouldBeEqual_WhenComponentsMatch()
    {
        var a = new Money(42m, "EUR");
        var b = new Money(42m, "EUR");

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenOtherIsNull()
    {
        var a = new Money(1m, "TRY");
        Money? b = null;

        Assert.False(a.Equals(b));
        Assert.False(a == b);
        Assert.False(b == a);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenOtherIsDifferentType()
    {
        var money = new Money(1m, "TRY");
        object other = "not a value object";

        Assert.False(money.Equals(other));
    }
```

Also add `using System.Collections.Generic;` at the top if not already present.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~ValueObjectTests`
Expected: FAIL — `error CS0246: The type or namespace name 'ValueObject' could not be found`.

- [ ] **Step 3: Create `ValueObject.cs`**

Create `src/Moongazing.OrionGuard/Domain/Primitives/ValueObject.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Base class for Domain-Driven Design value objects whose equality is determined by the values
/// of their components, not by reference identity.
/// </summary>
public abstract class ValueObject : IValueObject, IEquatable<ValueObject>
{
    /// <summary>
    /// Enumerates the components that participate in equality comparison. Override to yield the
    /// values that constitute the identity of this value object.
    /// </summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => obj is ValueObject vo && Equals(vo);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents())
        {
            hash.Add(component);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(ValueObject? left, ValueObject? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~ValueObjectTests`
Expected: PASS — 6 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Primitives/ValueObject.cs \
        tests/Moongazing.OrionGuard.Tests/ValueObjectTests.cs
git commit -m "feat(domain): add ValueObject base class with component-wise equality"
```

---

## Task 3: `IDomainEvent` interface (minimal stub for AggregateRoot)

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Events/IDomainEvent.cs`

- [ ] **Step 1: Create the interface**

Create `src/Moongazing.OrionGuard/Domain/Events/IDomainEvent.cs`:

```csharp
using System;

namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>
/// Represents a domain event raised by an aggregate root. Ships as an interface in v6.1.0;
/// the accompanying <c>DomainEventBase</c> record, <c>IDomainEventDispatcher</c>, and MediatR
/// bridge arrive in v6.2.0.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Globally unique identifier for this event instance.</summary>
    Guid EventId { get; }

    /// <summary>Timestamp in UTC at which the event was raised.</summary>
    DateTime OccurredOnUtc { get; }
}
```

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Events/IDomainEvent.cs
git commit -m "feat(domain): add IDomainEvent interface used by AggregateRoot"
```

(No dedicated test — exercised by `AggregateRootTests` in Task 10.)

---

## Task 4: `IBusinessRule` and `IAsyncBusinessRule` interfaces

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Rules/IBusinessRule.cs`
- Create: `src/Moongazing.OrionGuard/Domain/Rules/IAsyncBusinessRule.cs`

- [ ] **Step 1: Create `IBusinessRule.cs`**

Create `src/Moongazing.OrionGuard/Domain/Rules/IBusinessRule.cs`:

```csharp
namespace Moongazing.OrionGuard.Domain.Rules;

/// <summary>
/// A synchronous domain business rule. In v6.1.0 only the interface ships so that
/// <c>Entity.CheckRule</c> can enforce invariants; the <c>BusinessRule</c> abstract base class,
/// <c>Guard.Against.BrokenRule</c>, and <c>Validate.Rule</c> helpers arrive in v6.3.0.
/// </summary>
public interface IBusinessRule
{
    /// <summary>Returns <see langword="true"/> if this rule is currently violated.</summary>
    bool IsBroken();

    /// <summary>Localization key for the rule's message (looked up in <c>ValidationMessages</c>).</summary>
    string MessageKey { get; }

    /// <summary>Fallback message used when no translation is registered for <see cref="MessageKey"/>.</summary>
    string DefaultMessage { get; }

    /// <summary>Optional format arguments for the localized message.</summary>
    object[]? MessageArgs => null;
}
```

- [ ] **Step 2: Create `IAsyncBusinessRule.cs`**

Create `src/Moongazing.OrionGuard/Domain/Rules/IAsyncBusinessRule.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace Moongazing.OrionGuard.Domain.Rules;

/// <summary>
/// An asynchronous business rule — useful when rule evaluation requires I/O (e.g., uniqueness
/// checks against a repository).
/// </summary>
public interface IAsyncBusinessRule
{
    Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default);

    string MessageKey { get; }
    string DefaultMessage { get; }
    object[]? MessageArgs => null;
}
```

- [ ] **Step 3: Verify the project builds**

Run: `dotnet build src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Rules/IBusinessRule.cs \
        src/Moongazing.OrionGuard/Domain/Rules/IAsyncBusinessRule.cs
git commit -m "feat(domain): add IBusinessRule and IAsyncBusinessRule interfaces"
```

---

## Task 5: `BusinessRuleValidationException` and `DomainInvariantException`

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Exceptions/BusinessRuleValidationException.cs`
- Create: `src/Moongazing.OrionGuard/Domain/Exceptions/DomainInvariantException.cs`

- [ ] **Step 1: Create `BusinessRuleValidationException.cs`**

Create `src/Moongazing.OrionGuard/Domain/Exceptions/BusinessRuleValidationException.cs`:

```csharp
using System;
using Moongazing.OrionGuard.Domain.Rules;
using Moongazing.OrionGuard.Localization;

namespace Moongazing.OrionGuard.Domain.Exceptions;

/// <summary>
/// Thrown when a synchronous or asynchronous <see cref="IBusinessRule"/> / <see cref="IAsyncBusinessRule"/>
/// reports that it is broken.
/// </summary>
public sealed class BusinessRuleValidationException : Exception
{
    public string MessageKey { get; }
    public string RuleName { get; }

    public BusinessRuleValidationException(IBusinessRule rule)
        : base(Resolve(rule.MessageKey, rule.DefaultMessage, rule.MessageArgs))
    {
        MessageKey = rule.MessageKey;
        RuleName = rule.GetType().Name;
    }

    public BusinessRuleValidationException(IAsyncBusinessRule rule)
        : base(Resolve(rule.MessageKey, rule.DefaultMessage, rule.MessageArgs))
    {
        MessageKey = rule.MessageKey;
        RuleName = rule.GetType().Name;
    }

    private static string Resolve(string key, string fallback, object[]? args)
    {
        var localized = args is null
            ? ValidationMessages.Get(key)
            : ValidationMessages.Get(key, args);

        // ValidationMessages.Get returns the key itself when no translation is registered.
        return string.Equals(localized, key, StringComparison.Ordinal) ? fallback : localized;
    }
}
```

- [ ] **Step 2: Create `DomainInvariantException.cs`**

Create `src/Moongazing.OrionGuard/Domain/Exceptions/DomainInvariantException.cs`:

```csharp
using System;

namespace Moongazing.OrionGuard.Domain.Exceptions;

/// <summary>
/// Thrown when a domain invariant (as opposed to a named business rule) is violated, for example
/// when an aggregate receives an inconsistent combination of arguments.
/// </summary>
public sealed class DomainInvariantException : Exception
{
    public DomainInvariantException(string message) : base(message) { }
    public DomainInvariantException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 3: Verify the project builds**

Run: `dotnet build src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Exceptions/BusinessRuleValidationException.cs \
        src/Moongazing.OrionGuard/Domain/Exceptions/DomainInvariantException.cs
git commit -m "feat(domain): add BusinessRuleValidationException and DomainInvariantException"
```

---

## Task 6: `Entity<TId>` with identity equality

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Primitives/Entity.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/EntityTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Moongazing.OrionGuard.Tests/EntityTests.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class EntityTests
{
    private sealed class Customer : Entity<int>
    {
        public Customer(int id) : base(id) { }
        public Customer() { }
    }

    [Fact]
    public void Equals_ShouldReturnTrue_WhenIdsMatch()
    {
        var a = new Customer(1);
        var b = new Customer(1);

        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenIdsDiffer()
    {
        var a = new Customer(1);
        var b = new Customer(2);

        Assert.False(a.Equals(b));
        Assert.True(a != b);
    }

    [Fact]
    public void Ctor_ShouldThrow_WhenIdIsNullReferenceType()
    {
        Assert.Throws<ArgumentNullException>(() => new ReferenceIdEntity(null!));
    }

    [Fact]
    public void ParameterlessCtor_ShouldBeUsable_ForSerializers()
    {
        var customer = new Customer();

        Assert.Equal(0, customer.Id);
    }

    private sealed class ReferenceIdEntity : Entity<string>
    {
        public ReferenceIdEntity(string id) : base(id) { }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~EntityTests`
Expected: FAIL — `error CS0246: The type or namespace name 'Entity<>' could not be found`.

- [ ] **Step 3: Create `Entity.cs`**

Create `src/Moongazing.OrionGuard/Domain/Primitives/Entity.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moongazing.OrionGuard.Domain.Exceptions;
using Moongazing.OrionGuard.Domain.Rules;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Base class for Domain-Driven Design entities — objects whose identity persists across state
/// changes. Equality is determined by <see cref="Id"/>, not by the values of other properties.
/// </summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    protected Entity(TId id)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));
        Id = id;
    }

    /// <summary>Parameterless constructor for EF Core / serialization scenarios.</summary>
    protected Entity() { }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return EqualityComparer<TId>.Default.Equals(Id, other.Id);
    }

    public override bool Equals(object? obj) => obj is Entity<TId> e && Equals(e);

    public override int GetHashCode() =>
        EqualityComparer<TId>.Default.GetHashCode(Id!);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);

    /// <summary>
    /// Enforces a synchronous business rule. Throws <see cref="BusinessRuleValidationException"/>
    /// if the rule reports itself as broken.
    /// </summary>
    protected static void CheckRule(IBusinessRule rule)
    {
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        if (rule.IsBroken())
        {
            throw new BusinessRuleValidationException(rule);
        }
    }

    /// <summary>Enforces an asynchronous business rule.</summary>
    protected static async Task CheckRuleAsync(IAsyncBusinessRule rule, CancellationToken cancellationToken = default)
    {
        if (rule is null) throw new ArgumentNullException(nameof(rule));
        if (await rule.IsBrokenAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessRuleValidationException(rule);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~EntityTests`
Expected: PASS — 4 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Primitives/Entity.cs \
        tests/Moongazing.OrionGuard.Tests/EntityTests.cs
git commit -m "feat(domain): add Entity<TId> base class with identity equality and CheckRule"
```

---

## Task 7: Add test coverage for `Entity.CheckRule` (sync + async)

**Files:**
- Modify: `tests/Moongazing.OrionGuard.Tests/EntityTests.cs`

- [ ] **Step 1: Append failing tests**

Append to `tests/Moongazing.OrionGuard.Tests/EntityTests.cs` (inside the class, before the closing brace of `EntityTests`):

```csharp
    private sealed class AlwaysBrokenRule : IBusinessRule
    {
        public bool IsBroken() => true;
        public string MessageKey => "TestBrokenRule";
        public string DefaultMessage => "Rule intentionally broken for test.";
    }

    private sealed class NeverBrokenRule : IBusinessRule
    {
        public bool IsBroken() => false;
        public string MessageKey => "TestOkRule";
        public string DefaultMessage => "Never thrown.";
    }

    private sealed class AlwaysBrokenAsyncRule : IAsyncBusinessRule
    {
        public Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public string MessageKey => "TestAsyncBrokenRule";
        public string DefaultMessage => "Async rule broken.";
    }

    private sealed class CustomerWithRules : Entity<int>
    {
        public CustomerWithRules(int id) : base(id) { }

        public void EnforceSync(IBusinessRule rule) => CheckRule(rule);

        public Task EnforceAsync(IAsyncBusinessRule rule, CancellationToken ct = default)
            => CheckRuleAsync(rule, ct);
    }

    [Fact]
    public void CheckRule_ShouldThrowBusinessRuleValidationException_WhenRuleIsBroken()
    {
        var entity = new CustomerWithRules(1);

        var ex = Assert.Throws<BusinessRuleValidationException>(() => entity.EnforceSync(new AlwaysBrokenRule()));
        Assert.Equal("TestBrokenRule", ex.MessageKey);
        Assert.Equal(nameof(AlwaysBrokenRule), ex.RuleName);
        Assert.Equal("Rule intentionally broken for test.", ex.Message);
    }

    [Fact]
    public void CheckRule_ShouldNotThrow_WhenRuleIsNotBroken()
    {
        var entity = new CustomerWithRules(1);

        var ex = Record.Exception(() => entity.EnforceSync(new NeverBrokenRule()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task CheckRuleAsync_ShouldThrowBusinessRuleValidationException_WhenAsyncRuleIsBroken()
    {
        var entity = new CustomerWithRules(1);

        await Assert.ThrowsAsync<BusinessRuleValidationException>(
            () => entity.EnforceAsync(new AlwaysBrokenAsyncRule()));
    }
```

Add these top-level usings at the top of the file:

```csharp
using System.Threading;
using System.Threading.Tasks;
using Moongazing.OrionGuard.Domain.Exceptions;
using Moongazing.OrionGuard.Domain.Rules;
```

- [ ] **Step 2: Run tests to verify they pass (no new production code needed)**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~EntityTests`
Expected: PASS — 7 tests passed total (4 existing + 3 new).

- [ ] **Step 3: Commit**

```bash
git add tests/Moongazing.OrionGuard.Tests/EntityTests.cs
git commit -m "test(domain): cover Entity.CheckRule sync and async happy/broken paths"
```

---

## Task 8: `IAggregateRoot` marker + `AggregateRoot<TId>`

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Primitives/IAggregateRoot.cs`
- Create: `src/Moongazing.OrionGuard/Domain/Primitives/AggregateRoot.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/AggregateRootTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Moongazing.OrionGuard.Tests/AggregateRootTests.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class AggregateRootTests
{
    private sealed class OrderPlaced : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
    }

    private sealed class Order : AggregateRoot<int>
    {
        public Order(int id) : base(id) { }

        public void Place() => RaiseEvent(new OrderPlaced());
        public void RaiseNull() => RaiseEvent(null!);
    }

    [Fact]
    public void RaiseEvent_ShouldAppendToDomainEvents_WhenCalled()
    {
        var order = new Order(1);
        order.Place();
        order.Place();

        Assert.Equal(2, order.DomainEvents.Count);
    }

    [Fact]
    public void PullDomainEvents_ShouldReturnAndClear_WhenCalled()
    {
        var order = new Order(1);
        order.Place();
        order.Place();

        var pulled = order.PullDomainEvents();

        Assert.Equal(2, pulled.Count);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void PullDomainEvents_ShouldReturnEmpty_WhenNoEventsRaised()
    {
        var order = new Order(1);

        var pulled = order.PullDomainEvents();

        Assert.Empty(pulled);
    }

    [Fact]
    public void RaiseEvent_ShouldThrow_WhenEventIsNull()
    {
        var order = new Order(1);

        Assert.Throws<ArgumentNullException>(order.RaiseNull);
    }

    [Fact]
    public void IAggregateRoot_ShouldBeAssignableFromAggregateRootOfT()
    {
        var order = new Order(1);

        Assert.IsAssignableFrom<IAggregateRoot>(order);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~AggregateRootTests`
Expected: FAIL — `error CS0246: The type or namespace name 'AggregateRoot<>' could not be found`.

- [ ] **Step 3: Create `IAggregateRoot.cs`**

Create `src/Moongazing.OrionGuard/Domain/Primitives/IAggregateRoot.cs`:

```csharp
using System.Collections.Generic;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Non-generic marker interface for aggregate roots. Enables consumers (e.g., the EF Core
/// interceptor in the MediatR package) to discover aggregates via <c>ChangeTracker.Entries</c>
/// without knowing the identifier type.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>Domain events currently queued on this aggregate.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Returns the queued events and clears the internal buffer atomically.</summary>
    IReadOnlyCollection<IDomainEvent> PullDomainEvents();
}
```

- [ ] **Step 4: Create `AggregateRoot.cs`**

Create `src/Moongazing.OrionGuard/Domain/Primitives/AggregateRoot.cs`:

```csharp
using System;
using System.Collections.Generic;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Base class for DDD aggregate roots — entities that own a cluster of related objects and
/// serve as the single transactional boundary.
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected AggregateRoot(TId id) : base(id) { }
    protected AggregateRoot() { }

    /// <summary>Queues a domain event to be dispatched after the unit of work completes.</summary>
    protected void RaiseEvent(IDomainEvent @event)
    {
        if (@event is null) throw new ArgumentNullException(nameof(@event));
        _domainEvents.Add(@event);
    }

    /// <summary>
    /// Returns a snapshot of the queued events and clears the internal buffer. Intended to be
    /// called by a dispatcher immediately before publishing.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> PullDomainEvents()
    {
        var snapshot = _domainEvents.ToArray();
        _domainEvents.Clear();
        return snapshot;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~AggregateRootTests`
Expected: PASS — 5 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Primitives/IAggregateRoot.cs \
        src/Moongazing.OrionGuard/Domain/Primitives/AggregateRoot.cs \
        tests/Moongazing.OrionGuard.Tests/AggregateRootTests.cs
git commit -m "feat(domain): add AggregateRoot<TId> with event buffering via IAggregateRoot"
```

---

## Task 9: `StronglyTypedId<TValue>` base record

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Primitives/StronglyTypedId.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/StronglyTypedIdTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Moongazing.OrionGuard.Tests/StronglyTypedIdTests.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class StronglyTypedIdTests
{
    private sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
    private sealed record CustomerId(Guid Value) : StronglyTypedId<Guid>(Value);
    private sealed record IntegerId(int Value) : StronglyTypedId<int>(Value);

    [Fact]
    public void Equals_ShouldReturnTrue_WhenValuesAndTypesMatch()
    {
        var g = Guid.NewGuid();
        var a = new OrderId(g);
        var b = new OrderId(g);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenTypesDiffer()
    {
        var g = Guid.NewGuid();
        OrderId a = new(g);
        CustomerId b = new(g);

        Assert.NotEqual<object>(a, b);
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenValuesDiffer()
    {
        var a = new IntegerId(1);
        var b = new IntegerId(2);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ToString_ShouldIncludeValue_WhenCalled()
    {
        var id = new IntegerId(42);

        Assert.Contains("42", id.ToString());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~StronglyTypedIdTests`
Expected: FAIL — `error CS0246: The type or namespace name 'StronglyTypedId<>' could not be found`.

- [ ] **Step 3: Create `StronglyTypedId.cs`**

Create `src/Moongazing.OrionGuard/Domain/Primitives/StronglyTypedId.cs`:

```csharp
using System;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Base record for strongly-typed identifiers — avoids primitive obsession when modeling
/// domain identities. Equality and <c>GetHashCode</c> are provided by the <see langword="record"/>
/// compiler synthesis; derived types contribute type identity so that <c>OrderId(guid)</c> and
/// <c>CustomerId(guid)</c> with the same <see cref="Value"/> are <b>not</b> equal.
/// </summary>
/// <typeparam name="TValue">The underlying primitive type (typically <see cref="Guid"/>,
/// <see cref="int"/>, <see cref="long"/>, or <see cref="string"/>).</typeparam>
public abstract record StronglyTypedId<TValue>(TValue Value)
    where TValue : notnull, IEquatable<TValue>;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~StronglyTypedIdTests`
Expected: PASS — 4 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Primitives/StronglyTypedId.cs \
        tests/Moongazing.OrionGuard.Tests/StronglyTypedIdTests.cs
git commit -m "feat(domain): add StronglyTypedId<TValue> base record"
```

---

## Task 10: Add 3 new localization keys to all 14 languages

**Files:**
- Modify: `src/Moongazing.OrionGuard/Localization/ValidationMessages.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/DomainLocalizationTests.cs`

**Keys to add:**

| Key | EN |
|-----|-----|
| `DefaultStronglyTypedId` | `{0} must not be the default value.` |
| `BusinessRuleBroken` | `Business rule broken: {0}.` |
| `DomainInvariantViolated` | `Domain invariant violated: {0}.` |

- [ ] **Step 1: Write the failing test**

Create `tests/Moongazing.OrionGuard.Tests/DomainLocalizationTests.cs`:

```csharp
using System.Globalization;
using Moongazing.OrionGuard.Localization;

namespace Moongazing.OrionGuard.Tests;

public class DomainLocalizationTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("ar")]
    [InlineData("ja")]
    [InlineData("it")]
    [InlineData("zh")]
    [InlineData("ko")]
    [InlineData("ru")]
    [InlineData("nl")]
    [InlineData("pl")]
    public void ValidationMessages_ShouldResolveDomainKeys_ForEveryBundledLanguage(string culture)
    {
        var ci = new CultureInfo(culture);

        var defaultIdMsg = ValidationMessages.Get("DefaultStronglyTypedId", ci, "OrderId");
        var brokenRuleMsg = ValidationMessages.Get("BusinessRuleBroken", ci, "SomeRule");
        var invariantMsg = ValidationMessages.Get("DomainInvariantViolated", ci, "SomeInvariant");

        Assert.NotEqual("DefaultStronglyTypedId", defaultIdMsg);
        Assert.NotEqual("BusinessRuleBroken", brokenRuleMsg);
        Assert.NotEqual("DomainInvariantViolated", invariantMsg);

        Assert.Contains("OrderId", defaultIdMsg);
        Assert.Contains("SomeRule", brokenRuleMsg);
        Assert.Contains("SomeInvariant", invariantMsg);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~DomainLocalizationTests`
Expected: FAIL — the helper returns the key itself for unregistered keys; all assertions in the first language iteration fail.

- [ ] **Step 3: Add the three keys to each language dictionary**

Open `src/Moongazing.OrionGuard/Localization/ValidationMessages.cs`. Inside each language block (`["en"] = new(...)`, `["tr"] = new(...)`, ..., `["it"] = new(...)`), add three new entries immediately after the last existing entry (`"LicensePlate"`). Use the translations below (insert before the closing `})` of each dictionary):

**English** (`["en"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} must not be the default value.",
                ["BusinessRuleBroken"] = "Business rule broken: {0}.",
                ["DomainInvariantViolated"] = "Domain invariant violated: {0}."
```

**Turkish** (`["tr"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} varsayılan değer olmamalıdır.",
                ["BusinessRuleBroken"] = "İş kuralı ihlal edildi: {0}.",
                ["DomainInvariantViolated"] = "Alan değişmezi ihlal edildi: {0}."
```

**German** (`["de"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} darf nicht der Standardwert sein.",
                ["BusinessRuleBroken"] = "Geschäftsregel verletzt: {0}.",
                ["DomainInvariantViolated"] = "Domäneninvariante verletzt: {0}."
```

**French** (`["fr"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} ne doit pas être la valeur par défaut.",
                ["BusinessRuleBroken"] = "Règle métier violée : {0}.",
                ["DomainInvariantViolated"] = "Invariant de domaine violé : {0}."
```

**Spanish** (`["es"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} no debe ser el valor predeterminado.",
                ["BusinessRuleBroken"] = "Regla de negocio infringida: {0}.",
                ["DomainInvariantViolated"] = "Invariante de dominio infringida: {0}."
```

**Portuguese** (`["pt"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} não pode ser o valor padrão.",
                ["BusinessRuleBroken"] = "Regra de negócio violada: {0}.",
                ["DomainInvariantViolated"] = "Invariante de domínio violada: {0}."
```

**Arabic** (`["ar"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} يجب ألا يكون القيمة الافتراضية.",
                ["BusinessRuleBroken"] = "تم انتهاك قاعدة عمل: {0}.",
                ["DomainInvariantViolated"] = "تم انتهاك ثابت النطاق: {0}."
```

**Japanese** (`["ja"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} は既定値であってはなりません。",
                ["BusinessRuleBroken"] = "ビジネスルール違反: {0}。",
                ["DomainInvariantViolated"] = "ドメイン不変条件違反: {0}。"
```

**Italian** (`["it"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} non deve essere il valore predefinito.",
                ["BusinessRuleBroken"] = "Regola di business violata: {0}.",
                ["DomainInvariantViolated"] = "Invariante di dominio violata: {0}."
```

**Chinese** (`["zh"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} 不能为默认值。",
                ["BusinessRuleBroken"] = "业务规则被破坏：{0}。",
                ["DomainInvariantViolated"] = "领域不变式被破坏：{0}。"
```

**Korean** (`["ko"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0}은(는) 기본값이 아니어야 합니다.",
                ["BusinessRuleBroken"] = "비즈니스 규칙 위반: {0}.",
                ["DomainInvariantViolated"] = "도메인 불변식 위반: {0}."
```

**Russian** (`["ru"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} не должен быть значением по умолчанию.",
                ["BusinessRuleBroken"] = "Нарушено бизнес-правило: {0}.",
                ["DomainInvariantViolated"] = "Нарушен инвариант домена: {0}."
```

**Dutch** (`["nl"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} mag niet de standaardwaarde zijn.",
                ["BusinessRuleBroken"] = "Bedrijfsregel geschonden: {0}.",
                ["DomainInvariantViolated"] = "Domein-invariant geschonden: {0}."
```

**Polish** (`["pl"]`):
```csharp
                ["DefaultStronglyTypedId"] = "{0} nie może być wartością domyślną.",
                ["BusinessRuleBroken"] = "Naruszono regułę biznesową: {0}.",
                ["DomainInvariantViolated"] = "Naruszono niezmiennik domeny: {0}."
```

**Note:** Each language block currently ends with `["LicensePlate"] = "...."` (no trailing comma). When inserting the new entries, change the previous closing line to add a comma after `"LicensePlate"` entry and keep the newly added last entry without a trailing comma.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~DomainLocalizationTests`
Expected: PASS — 14 theory rows passed.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard/Localization/ValidationMessages.cs \
        tests/Moongazing.OrionGuard.Tests/DomainLocalizationTests.cs
git commit -m "feat(localization): add DDD domain keys for all 14 bundled languages"
```

---

## Task 11: `Guard.Against.DefaultStronglyTypedId` extension

**Files:**
- Create: `src/Moongazing.OrionGuard/Extensions/StronglyTypedIdGuards.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/StronglyTypedIdGuardTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Moongazing.OrionGuard.Tests/StronglyTypedIdGuardTests.cs`:

```csharp
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

public class StronglyTypedIdGuardTests
{
    private sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
    private sealed record IntId(int Value) : StronglyTypedId<int>(Value);
    private sealed record StringId(string Value) : StronglyTypedId<string>(Value);

    [Fact]
    public void DefaultStronglyTypedId_ShouldThrow_WhenIdIsNull()
    {
        OrderId? id = null;
        Assert.Throws<NullValueException>(() => Guard.Against.DefaultStronglyTypedId(id!, nameof(id)));
    }

    [Fact]
    public void DefaultStronglyTypedId_ShouldThrow_WhenGuidValueIsEmpty()
    {
        var id = new OrderId(Guid.Empty);
        Assert.Throws<DefaultValueException>(() => Guard.Against.DefaultStronglyTypedId(id, nameof(id)));
    }

    [Fact]
    public void DefaultStronglyTypedId_ShouldNotThrow_WhenGuidValueIsNotEmpty()
    {
        var id = new OrderId(Guid.NewGuid());
        var ex = Record.Exception(() => Guard.Against.DefaultStronglyTypedId(id, nameof(id)));
        Assert.Null(ex);
    }

    [Fact]
    public void DefaultStronglyTypedId_ShouldThrow_WhenIntValueIsZero()
    {
        var id = new IntId(0);
        Assert.Throws<DefaultValueException>(() => Guard.Against.DefaultStronglyTypedId(id, nameof(id)));
    }

    [Fact]
    public void DefaultStronglyTypedId_ShouldThrow_WhenStringValueIsNullOrEmpty()
    {
        var id = new StringId(string.Empty);
        Assert.Throws<DefaultValueException>(() => Guard.Against.DefaultStronglyTypedId(id, nameof(id)));
    }

    [Fact]
    public void DefaultStronglyTypedId_ShouldReturnInstance_ForChaining()
    {
        var id = new IntId(7);
        var returned = Guard.Against.DefaultStronglyTypedId(id, nameof(id));
        Assert.Same(id, returned);
    }
}
```

- [ ] **Step 2: Inspect existing `Guard.Against` entry point and exception types**

Run: `dotnet build src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj` to confirm the current `Guard` class, `Guard.Against` property, and exception names. In case `DefaultValueException` does not exist under `Moongazing.OrionGuard.Exceptions`, adapt the step below to the nearest existing exception (e.g., `InvalidValueException`) — do **not** invent a new exception type.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~StronglyTypedIdGuardTests`
Expected: FAIL — `DefaultStronglyTypedId` does not exist on `Guard.Against`.

- [ ] **Step 4: Create the guard extension**

Create `src/Moongazing.OrionGuard/Extensions/StronglyTypedIdGuards.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Localization;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Guard extensions for <see cref="StronglyTypedId{TValue}"/>-derived types.
/// </summary>
public static class StronglyTypedIdGuards
{
    /// <summary>
    /// Throws when <paramref name="id"/> is <see langword="null"/> or its wrapped value equals
    /// the default of its underlying type (<see cref="Guid.Empty"/>, <c>0</c>, <c>""</c>).
    /// </summary>
    public static TId DefaultStronglyTypedId<TId, TValue>(
        this IGuardClause guard,
        TId id,
        [CallerArgumentExpression(nameof(id))] string? parameterName = null)
        where TId : StronglyTypedId<TValue>
        where TValue : notnull, IEquatable<TValue>
    {
        if (id is null)
        {
            throw new NullValueException(parameterName ?? nameof(id),
                ValidationMessages.Get("NotNull", parameterName ?? nameof(id)));
        }

        if (EqualityComparer<TValue>.Default.Equals(id.Value, default!) ||
            (id.Value is string s && string.IsNullOrEmpty(s)))
        {
            throw new DefaultValueException(parameterName ?? nameof(id),
                ValidationMessages.Get("DefaultStronglyTypedId", parameterName ?? nameof(id)));
        }

        return id;
    }
}
```

**Note on exception types:** If `DefaultValueException` does not exist, inspect `src/Moongazing.OrionGuard/Exceptions/` and use the closest existing "invalid value" exception; update this task's test file in Step 1 to match. Do **not** rename existing exceptions.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~StronglyTypedIdGuardTests`
Expected: PASS — 6 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/Moongazing.OrionGuard/Extensions/StronglyTypedIdGuards.cs \
        tests/Moongazing.OrionGuard.Tests/StronglyTypedIdGuardTests.cs
git commit -m "feat(guard): add Guard.Against.DefaultStronglyTypedId extension"
```

---

## Task 12: Scaffold source-generator project additions (attribute + placeholder)

**Files:**
- Create: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdAttribute.cs`
- Create: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/SupportedValueType.cs`

- [ ] **Step 1: Create `SupportedValueType.cs`**

Create `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/SupportedValueType.cs`:

```csharp
#nullable enable

namespace Moongazing.OrionGuard.Generators.StronglyTypedIds
{
    /// <summary>
    /// Enumerates the underlying primitive types supported by the <c>[StronglyTypedId]</c>
    /// generator.
    /// </summary>
    internal enum SupportedValueType
    {
        Guid,
        Int32,
        Int64,
        String,
        Ulid
    }

    internal static class SupportedValueTypeMap
    {
        public static bool TryParse(string fullyQualifiedName, out SupportedValueType result)
        {
            switch (fullyQualifiedName)
            {
                case "System.Guid":
                    result = SupportedValueType.Guid; return true;
                case "System.Int32":
                case "int":
                    result = SupportedValueType.Int32; return true;
                case "System.Int64":
                case "long":
                    result = SupportedValueType.Int64; return true;
                case "System.String":
                case "string":
                    result = SupportedValueType.String; return true;
                case "System.Ulid":
                    result = SupportedValueType.Ulid; return true;
                default:
                    result = default;
                    return false;
            }
        }

        public static string CSharpKeyword(SupportedValueType type) => type switch
        {
            SupportedValueType.Guid => "global::System.Guid",
            SupportedValueType.Int32 => "int",
            SupportedValueType.Int64 => "long",
            SupportedValueType.String => "string",
            SupportedValueType.Ulid => "global::System.Ulid",
            _ => throw new System.ArgumentOutOfRangeException(nameof(type))
        };
    }
}
```

- [ ] **Step 2: Create `StronglyTypedIdAttribute.cs`** (source injected into consumer compilations)

Create `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdAttribute.cs`:

```csharp
#nullable enable

namespace Moongazing.OrionGuard.Generators.StronglyTypedIds
{
    /// <summary>
    /// Text source of the <c>[StronglyTypedId]</c> attribute — injected into each compilation
    /// via <c>RegisterPostInitializationOutput</c>.
    /// </summary>
    internal static class StronglyTypedIdAttributeSource
    {
        public const string FullName = "Moongazing.OrionGuard.Domain.Primitives.StronglyTypedIdAttribute";

        public const string Source = @"// <auto-generated/>
#nullable enable

namespace Moongazing.OrionGuard.Domain.Primitives
{
    /// <summary>
    /// Marks a readonly partial struct as a strongly-typed id backed by the specified primitive type.
    /// The OrionGuard source generator emits equality, comparison, conversion members, as well as
    /// EF Core ValueConverter, System.Text.Json converter, and TypeConverter companion types.
    /// </summary>
    /// <typeparam name=""TValue"">Underlying primitive type. Supported: System.Guid, int, long,
    /// string, System.Ulid (net9.0+).</typeparam>
    [System.AttributeUsage(System.AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    internal sealed class StronglyTypedIdAttribute<TValue> : System.Attribute { }
}
";
    }
}
```

- [ ] **Step 3: Verify Generators project builds**

Run: `dotnet build src/Moongazing.OrionGuard.Generators/Moongazing.OrionGuard.Generators.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Moongazing.OrionGuard.Generators/StronglyTypedIds/
git commit -m "feat(generators): scaffold StronglyTypedId generator — attribute source and type map"
```

---

## Task 13: Implement `StronglyTypedIdGenerator` — core partial emission

**Files:**
- Create: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs`
- Create: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdEmitter.cs`
- Create: `tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj`
- Create: `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs`
- Modify: `Moongazing.OrionGuard.sln` (add new test project)

- [ ] **Step 1: Create the test project `csproj`**

Create `tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.2" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Moongazing.OrionGuard.Generators\Moongazing.OrionGuard.Generators.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="true" />
    <ProjectReference Include="..\..\src\Moongazing.OrionGuard\Moongazing.OrionGuard.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add the test project to the solution**

Run from `OrionGuard/`:
```bash
dotnet sln Moongazing.OrionGuard.sln add tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj
```
Expected: `Project ... added to the solution.`

- [ ] **Step 3: Write the failing test (core partial emission only)**

Create `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Generators.StronglyTypedIds;

namespace Moongazing.OrionGuard.Generators.Tests;

public class StronglyTypedIdGeneratorTests
{
    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new StronglyTypedIdGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    [Fact]
    public void Generator_ShouldEmitPartialStructWithValueField_ForGuidBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct OrderId { }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        var generated = result.Results.SelectMany(r => r.GeneratedSources).ToArray();
        Assert.Contains(generated, s => s.HintName.Contains("OrderId"));
        var body = generated.First(s => s.HintName.Contains("OrderId")).SourceText.ToString();
        Assert.Contains("public readonly partial struct OrderId", body);
        Assert.Contains("global::System.Guid Value", body);
    }

    [Fact]
    public void Generator_ShouldEmitAttributeSourceIntoCompilation()
    {
        const string source = "namespace App { public class Empty { } }";

        var result = RunGenerator(source);

        var attributeSource = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("StronglyTypedIdAttribute"));

        Assert.NotEqual(default, attributeSource);
        Assert.Contains("StronglyTypedIdAttribute", attributeSource.SourceText.ToString());
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj`
Expected: FAIL — `error CS0246: The type or namespace name 'StronglyTypedIdGenerator' could not be found`.

- [ ] **Step 5: Create `StronglyTypedIdEmitter.cs` (core partial body only)**

Create `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdEmitter.cs`:

```csharp
#nullable enable

using System.Text;

namespace Moongazing.OrionGuard.Generators.StronglyTypedIds
{
    internal static class StronglyTypedIdEmitter
    {
        public static string EmitPartial(string @namespace, string typeName, SupportedValueType valueType)
        {
            var valueCsKeyword = SupportedValueTypeMap.CSharpKeyword(valueType);
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.Append("namespace ").AppendLine(@namespace);
            sb.AppendLine("{");
            sb.Append("    public readonly partial struct ").Append(typeName)
              .Append(" : global::System.IEquatable<").Append(typeName).AppendLine(">")
              .AppendLine("    {");
            sb.Append("        public ").Append(valueCsKeyword).AppendLine(" Value { get; }");
            sb.AppendLine();
            sb.Append("        public ").Append(typeName).Append("(").Append(valueCsKeyword).AppendLine(" value) => Value = value;");
            sb.AppendLine();
            sb.Append("        public bool Equals(").Append(typeName).AppendLine(" other) => Value.Equals(other.Value);");
            sb.Append("        public override bool Equals(object? obj) => obj is ").Append(typeName).AppendLine(" other && Equals(other);");
            sb.AppendLine("        public override int GetHashCode() => Value.GetHashCode();");
            sb.AppendLine("        public override string ToString() => Value.ToString() ?? string.Empty;");
            sb.Append("        public static bool operator ==(").Append(typeName).Append(" left, ").Append(typeName).AppendLine(" right) => left.Equals(right);");
            sb.Append("        public static bool operator !=(").Append(typeName).Append(" left, ").Append(typeName).AppendLine(" right) => !left.Equals(right);");

            if (valueType == SupportedValueType.Guid)
            {
                sb.Append("        public static ").Append(typeName).AppendLine(" New() => new(global::System.Guid.NewGuid());");
                sb.Append("        public static ").Append(typeName).AppendLine(" Empty => new(global::System.Guid.Empty);");
            }
            else if (valueType == SupportedValueType.Ulid)
            {
                sb.AppendLine("#if NET9_0_OR_GREATER");
                sb.Append("        public static ").Append(typeName).AppendLine(" New() => new(global::System.Ulid.NewUlid());");
                sb.Append("        public static ").Append(typeName).AppendLine(" Empty => new(default(global::System.Ulid));");
                sb.AppendLine("#endif");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
```

- [ ] **Step 6: Create `StronglyTypedIdGenerator.cs`**

Create `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs`:

```csharp
#nullable enable

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Linq;
using System.Text;

namespace Moongazing.OrionGuard.Generators.StronglyTypedIds
{
    /// <summary>
    /// Incremental source generator for <c>[StronglyTypedId&lt;TValue&gt;]</c>-decorated readonly
    /// partial structs. Emits the type body plus EF Core / System.Text.Json / TypeConverter
    /// companions in subsequent generator passes.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class StronglyTypedIdGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(ctx =>
                ctx.AddSource(
                    "StronglyTypedIdAttribute.g.cs",
                    SourceText.From(StronglyTypedIdAttributeSource.Source, Encoding.UTF8)));

            var targets = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    StronglyTypedIdAttributeSource.FullName,
                    predicate: static (node, _) => node is StructDeclarationSyntax sds
                        && sds.Modifiers.Any(m => m.ValueText == "partial")
                        && sds.Modifiers.Any(m => m.ValueText == "readonly"),
                    transform: static (ctx, _) => Transform(ctx))
                .Where(static t => t is not null);

            context.RegisterSourceOutput(targets, static (spc, target) =>
            {
                if (target is null) return;

                var source = StronglyTypedIdEmitter.EmitPartial(
                    target.Namespace, target.TypeName, target.ValueType);

                spc.AddSource(
                    $"{target.TypeName}.StronglyTypedId.g.cs",
                    SourceText.From(source, Encoding.UTF8));
            });
        }

        private static StronglyTypedIdTarget? Transform(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol symbol) return null;

            var attribute = ctx.Attributes.FirstOrDefault();
            if (attribute is null || attribute.AttributeClass is null) return null;

            var typeArg = attribute.AttributeClass.TypeArguments.FirstOrDefault();
            if (typeArg is null) return null;

            var fqName = typeArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);

            if (!SupportedValueTypeMap.TryParse(fqName, out var mapped)) return null;

            return new StronglyTypedIdTarget(
                symbol.ContainingNamespace.ToDisplayString(),
                symbol.Name,
                mapped);
        }

        private sealed record StronglyTypedIdTarget(string Namespace, string TypeName, SupportedValueType ValueType);
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj`
Expected: PASS — 2 tests passed.

- [ ] **Step 8: Commit**

```bash
git add src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs \
        src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdEmitter.cs \
        tests/Moongazing.OrionGuard.Generators.Tests/ \
        Moongazing.OrionGuard.sln
git commit -m "feat(generators): incremental StronglyTypedId generator — partial struct emission"
```

---

## Task 14: Extend emitter with EF Core `ValueConverter`

**Files:**
- Create: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/EfCoreConverterEmitter.cs`
- Modify: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs`
- Modify: `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs`

- [ ] **Step 1: Append the failing test**

Append to `StronglyTypedIdGeneratorTests.cs` (inside the class):

```csharp
    [Fact]
    public void Generator_ShouldEmitEfCoreValueConverter_ForGuidBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct CustomerId { }
            }
            """;

        var result = RunGenerator(source);

        var converterSource = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("CustomerIdEfCoreValueConverter"));

        Assert.NotEqual(default, converterSource);
        var text = converterSource.SourceText.ToString();
        Assert.Contains("CustomerIdEfCoreValueConverter", text);
        Assert.Contains("Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter", text);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj --filter Generator_ShouldEmitEfCoreValueConverter_ForGuidBackedId`
Expected: FAIL — no generated file matches `CustomerIdEfCoreValueConverter`.

- [ ] **Step 3: Create `EfCoreConverterEmitter.cs`**

Create `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/EfCoreConverterEmitter.cs`:

```csharp
#nullable enable

using System.Text;

namespace Moongazing.OrionGuard.Generators.StronglyTypedIds
{
    internal static class EfCoreConverterEmitter
    {
        public static string Emit(string @namespace, string typeName, SupportedValueType valueType)
        {
            var valueCsKeyword = SupportedValueTypeMap.CSharpKeyword(valueType);
            var converterName = typeName + "EfCoreValueConverter";

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.Append("namespace ").AppendLine(@namespace);
            sb.AppendLine("{");
            sb.Append("    public sealed class ").Append(converterName)
              .Append(" : global::Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<")
              .Append(typeName).Append(", ").Append(valueCsKeyword).AppendLine(">");
            sb.AppendLine("    {");
            sb.Append("        public ").Append(converterName).AppendLine("()");
            sb.AppendLine("            : base(id => id.Value, value => new " + typeName + "(value)) { }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string HintName(string typeName) => typeName + "EfCoreValueConverter.g.cs";
    }
}
```

- [ ] **Step 4: Wire up the emitter in the generator**

In `StronglyTypedIdGenerator.cs`, replace the `RegisterSourceOutput` callback body with:

```csharp
            context.RegisterSourceOutput(targets, static (spc, target) =>
            {
                if (target is null) return;

                spc.AddSource(
                    $"{target.TypeName}.StronglyTypedId.g.cs",
                    SourceText.From(
                        StronglyTypedIdEmitter.EmitPartial(target.Namespace, target.TypeName, target.ValueType),
                        Encoding.UTF8));

                spc.AddSource(
                    EfCoreConverterEmitter.HintName(target.TypeName),
                    SourceText.From(
                        EfCoreConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                        Encoding.UTF8));
            });
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj`
Expected: PASS — 3 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/Moongazing.OrionGuard.Generators/StronglyTypedIds/EfCoreConverterEmitter.cs \
        src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs \
        tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs
git commit -m "feat(generators): emit EF Core ValueConverter for StronglyTypedId"
```

---

## Task 15: Extend emitter with `System.Text.Json.JsonConverter<T>`

**Files:**
- Create: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/JsonConverterEmitter.cs`
- Modify: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs`
- Modify: `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs`

- [ ] **Step 1: Append the failing test**

Append to `StronglyTypedIdGeneratorTests.cs`:

```csharp
    [Fact]
    public void Generator_ShouldEmitJsonConverter_ForGuidBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct ProductId { }
            }
            """;

        var result = RunGenerator(source);

        var jsonSource = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("ProductIdJsonConverter"));

        Assert.NotEqual(default, jsonSource);
        var text = jsonSource.SourceText.ToString();
        Assert.Contains("System.Text.Json.Serialization.JsonConverter<App.ProductId>", text);
        Assert.Contains("Read(", text);
        Assert.Contains("Write(", text);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj --filter Generator_ShouldEmitJsonConverter_ForGuidBackedId`
Expected: FAIL.

- [ ] **Step 3: Create `JsonConverterEmitter.cs`**

Create `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/JsonConverterEmitter.cs`:

```csharp
#nullable enable

using System.Text;

namespace Moongazing.OrionGuard.Generators.StronglyTypedIds
{
    internal static class JsonConverterEmitter
    {
        public static string Emit(string @namespace, string typeName, SupportedValueType valueType)
        {
            var (readMethod, writeMethod, valueCsKeyword) = valueType switch
            {
                SupportedValueType.Guid => ("reader.GetGuid()", "WriteStringValue(value.Value)", "global::System.Guid"),
                SupportedValueType.Int32 => ("reader.GetInt32()", "WriteNumberValue(value.Value)", "int"),
                SupportedValueType.Int64 => ("reader.GetInt64()", "WriteNumberValue(value.Value)", "long"),
                SupportedValueType.String => ("reader.GetString() ?? string.Empty", "WriteStringValue(value.Value)", "string"),
                SupportedValueType.Ulid => ("global::System.Ulid.Parse(reader.GetString() ?? string.Empty)", "WriteStringValue(value.Value.ToString())", "global::System.Ulid"),
                _ => ("default", "WriteNullValue()", "object")
            };

            var converterName = typeName + "JsonConverter";
            var fullyQualified = $"global::{@namespace}.{typeName}";

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.Append("namespace ").AppendLine(@namespace);
            sb.AppendLine("{");
            sb.Append("    public sealed class ").Append(converterName)
              .Append(" : global::System.Text.Json.Serialization.JsonConverter<")
              .Append(@namespace).Append(".").Append(typeName).AppendLine(">");
            sb.AppendLine("    {");
            sb.Append("        public override ").Append(typeName).AppendLine(" Read(");
            sb.AppendLine("            ref global::System.Text.Json.Utf8JsonReader reader,");
            sb.AppendLine("            global::System.Type typeToConvert,");
            sb.AppendLine("            global::System.Text.Json.JsonSerializerOptions options)");
            sb.AppendLine("        {");
            sb.Append("            return new ").Append(typeName).Append("(").Append(readMethod).AppendLine(");");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public override void Write(");
            sb.AppendLine("            global::System.Text.Json.Utf8JsonWriter writer,");
            sb.Append("            ").Append(typeName).AppendLine(" value,");
            sb.AppendLine("            global::System.Text.Json.JsonSerializerOptions options)");
            sb.AppendLine("        {");
            sb.Append("            writer.").Append(writeMethod).AppendLine(";");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string HintName(string typeName) => typeName + "JsonConverter.g.cs";
    }
}
```

- [ ] **Step 4: Wire up in `StronglyTypedIdGenerator.cs`**

Extend the `RegisterSourceOutput` callback by adding another `spc.AddSource` call:

```csharp
                spc.AddSource(
                    JsonConverterEmitter.HintName(target.TypeName),
                    SourceText.From(
                        JsonConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                        Encoding.UTF8));
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj`
Expected: PASS — 4 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/Moongazing.OrionGuard.Generators/StronglyTypedIds/JsonConverterEmitter.cs \
        src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs \
        tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs
git commit -m "feat(generators): emit System.Text.Json converter for StronglyTypedId"
```

---

## Task 16: Extend emitter with `TypeConverter`

**Files:**
- Create: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/TypeConverterEmitter.cs`
- Modify: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs`
- Modify: `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs`

- [ ] **Step 1: Append the failing test**

Append to `StronglyTypedIdGeneratorTests.cs`:

```csharp
    [Fact]
    public void Generator_ShouldEmitTypeConverter_ForGuidBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct InvoiceId { }
            }
            """;

        var result = RunGenerator(source);

        var tcSource = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("InvoiceIdTypeConverter"));

        Assert.NotEqual(default, tcSource);
        var text = tcSource.SourceText.ToString();
        Assert.Contains("System.ComponentModel.TypeConverter", text);
        Assert.Contains("ConvertFrom", text);
        Assert.Contains("ConvertTo", text);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj --filter Generator_ShouldEmitTypeConverter_ForGuidBackedId`
Expected: FAIL.

- [ ] **Step 3: Create `TypeConverterEmitter.cs`**

Create `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/TypeConverterEmitter.cs`:

```csharp
#nullable enable

using System.Text;

namespace Moongazing.OrionGuard.Generators.StronglyTypedIds
{
    internal static class TypeConverterEmitter
    {
        public static string Emit(string @namespace, string typeName, SupportedValueType valueType)
        {
            var valueCsKeyword = SupportedValueTypeMap.CSharpKeyword(valueType);
            var converterName = typeName + "TypeConverter";

            var parseExpr = valueType switch
            {
                SupportedValueType.Guid   => "global::System.Guid.Parse(s)",
                SupportedValueType.Int32  => "int.Parse(s, global::System.Globalization.CultureInfo.InvariantCulture)",
                SupportedValueType.Int64  => "long.Parse(s, global::System.Globalization.CultureInfo.InvariantCulture)",
                SupportedValueType.String => "s",
                SupportedValueType.Ulid   => "global::System.Ulid.Parse(s)",
                _ => "default"
            };

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.Append("namespace ").AppendLine(@namespace);
            sb.AppendLine("{");
            sb.Append("    public sealed class ").Append(converterName)
              .AppendLine(" : global::System.ComponentModel.TypeConverter");
            sb.AppendLine("    {");
            sb.AppendLine("        public override bool CanConvertFrom(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Type sourceType)");
            sb.AppendLine("            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);");
            sb.AppendLine();
            sb.AppendLine("        public override object? ConvertFrom(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Globalization.CultureInfo? culture, object value)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (value is string s)");
            sb.Append("                return new ").Append(typeName).Append("(").Append(parseExpr).AppendLine(");");
            sb.AppendLine("            return base.ConvertFrom(context, culture, value);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public override object? ConvertTo(global::System.ComponentModel.ITypeDescriptorContext? context, global::System.Globalization.CultureInfo? culture, object? value, global::System.Type destinationType)");
            sb.AppendLine("        {");
            sb.Append("            if (destinationType == typeof(string) && value is ").Append(typeName).AppendLine(" id)");
            sb.AppendLine("                return id.Value?.ToString() ?? string.Empty;");
            sb.AppendLine("            return base.ConvertTo(context, culture, value, destinationType);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string HintName(string typeName) => typeName + "TypeConverter.g.cs";
    }
}
```

- [ ] **Step 4: Wire up in `StronglyTypedIdGenerator.cs`**

Add to the `RegisterSourceOutput` callback:

```csharp
                spc.AddSource(
                    TypeConverterEmitter.HintName(target.TypeName),
                    SourceText.From(
                        TypeConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                        Encoding.UTF8));
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj`
Expected: PASS — 5 tests passed.

- [ ] **Step 6: Commit**

```bash
git add src/Moongazing.OrionGuard.Generators/StronglyTypedIds/TypeConverterEmitter.cs \
        src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs \
        tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs
git commit -m "feat(generators): emit TypeConverter for StronglyTypedId (ASP.NET Core binding)"
```

---

## Task 17: `AddOrionGuardStronglyTypedIds()` DI extension

**Files:**
- Create: `src/Moongazing.OrionGuard/DependencyInjection/StronglyTypedIdServiceExtensions.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/StronglyTypedIdServiceExtensionsTests.cs`

**Context:** The DI extension scans a set of caller-provided assemblies for all types ending with `EfCoreValueConverter` (convention produced by the generator) and registers each one as `ValueConverter<,>` in the service collection with `ServiceLifetime.Singleton`. This lets EF Core discover them via `services.GetServices<ValueConverter>()`.

- [ ] **Step 1: Write the failing test**

Create `tests/Moongazing.OrionGuard.Tests/StronglyTypedIdServiceExtensionsTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Tests;

public class StronglyTypedIdServiceExtensionsTests
{
    // Placeholder "generated" converter matching the generator's naming convention.
    public sealed class FakeStrongIdEfCoreValueConverter { }

    [Fact]
    public void AddOrionGuardStronglyTypedIds_ShouldRegisterDiscoveredConverters_WhenScanningAssembly()
    {
        var services = new ServiceCollection();

        services.AddOrionGuardStronglyTypedIds(typeof(StronglyTypedIdServiceExtensionsTests).Assembly);

        var registered = services.Where(d => d.ServiceType == typeof(FakeStrongIdEfCoreValueConverter));
        Assert.Single(registered);
    }

    [Fact]
    public void AddOrionGuardStronglyTypedIds_ShouldBeIdempotent_WhenCalledTwice()
    {
        var services = new ServiceCollection();

        services.AddOrionGuardStronglyTypedIds(typeof(StronglyTypedIdServiceExtensionsTests).Assembly);
        services.AddOrionGuardStronglyTypedIds(typeof(StronglyTypedIdServiceExtensionsTests).Assembly);

        var registered = services.Where(d => d.ServiceType == typeof(FakeStrongIdEfCoreValueConverter));
        Assert.Single(registered);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~StronglyTypedIdServiceExtensionsTests`
Expected: FAIL — `error CS0246: The type or namespace name 'AddOrionGuardStronglyTypedIds' / 'StronglyTypedIdServiceExtensions' could not be found`.

- [ ] **Step 3: Create `StronglyTypedIdServiceExtensions.cs`**

Create `src/Moongazing.OrionGuard/DependencyInjection/StronglyTypedIdServiceExtensions.cs`:

```csharp
using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Moongazing.OrionGuard.DependencyInjection;

/// <summary>
/// Registers source-generated EF Core value converters emitted by the
/// <c>[StronglyTypedId]</c> generator.
/// </summary>
public static class StronglyTypedIdServiceExtensions
{
    private const string ConverterSuffix = "EfCoreValueConverter";

    /// <summary>
    /// Scans the specified assemblies for generated EF Core value converters (named by the
    /// <c>[StronglyTypedId]</c> generator with the suffix <c>EfCoreValueConverter</c>) and
    /// registers each as a singleton of its concrete type.
    /// </summary>
    public static IServiceCollection AddOrionGuardStronglyTypedIds(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (assemblies is null || assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetCallingAssembly() };
        }

        foreach (var assembly in assemblies)
        {
            var converters = assembly.GetTypes()
                .Where(t => !t.IsAbstract
                            && !t.IsGenericTypeDefinition
                            && t.Name.EndsWith(ConverterSuffix, StringComparison.Ordinal));

            foreach (var converter in converters)
            {
                services.TryAddSingleton(converter);
            }
        }

        return services;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~StronglyTypedIdServiceExtensionsTests`
Expected: PASS — 2 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard/DependencyInjection/StronglyTypedIdServiceExtensions.cs \
        tests/Moongazing.OrionGuard.Tests/StronglyTypedIdServiceExtensionsTests.cs
git commit -m "feat(di): add AddOrionGuardStronglyTypedIds service collection extension"
```

---

## Task 18: Benchmark for DDD primitives

**Files:**
- Create: `benchmarks/Moongazing.OrionGuard.Benchmarks/DomainPrimitivesBenchmark.cs`
- Modify: `benchmarks/Moongazing.OrionGuard.Benchmarks/Program.cs`

- [ ] **Step 1: Inspect existing benchmark to match conventions**

Read: `benchmarks/Moongazing.OrionGuard.Benchmarks/NullCheckBenchmarks.cs` to confirm class attributes (`[MemoryDiagnoser]`, `[SimpleJob]`), `[Benchmark]` usage, and how `Program.cs` registers benchmarks.

- [ ] **Step 2: Create `DomainPrimitivesBenchmark.cs`**

Create `benchmarks/Moongazing.OrionGuard.Benchmarks/DomainPrimitivesBenchmark.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Benchmarks;

[MemoryDiagnoser]
public class DomainPrimitivesBenchmark
{
    private sealed class Money : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }

        public Money(decimal amount, string currency)
        {
            Amount = amount;
            Currency = currency;
        }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    private sealed record RecordMoney(decimal Amount, string Currency) : IValueObject;

    private sealed record OrderShippedEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
    }

    private sealed class Order : AggregateRoot<int>
    {
        public Order() : base(1) { }
        public void Ship() => RaiseEvent(new OrderShippedEvent());
    }

    private readonly Money _a = new(100m, "TRY");
    private readonly Money _b = new(100m, "TRY");
    private readonly RecordMoney _ra = new(100m, "TRY");
    private readonly RecordMoney _rb = new(100m, "TRY");
    private readonly Order _order = new();

    [Benchmark]
    public bool ValueObject_ClassEquality() => _a.Equals(_b);

    [Benchmark]
    public bool ValueObject_RecordEquality() => _ra.Equals(_rb);

    [Benchmark]
    public int AggregateRoot_RaiseAndPull()
    {
        _order.Ship();
        return _order.PullDomainEvents().Count;
    }
}
```

- [ ] **Step 3: Register the new benchmark**

Open `benchmarks/Moongazing.OrionGuard.Benchmarks/Program.cs` and add `DomainPrimitivesBenchmark` to the type list following existing convention (e.g., `BenchmarkSwitcher.FromTypes(new[] { typeof(NullCheckBenchmarks), ..., typeof(DomainPrimitivesBenchmark) }).Run(args);`).

- [ ] **Step 4: Verify the benchmark project compiles**

Run: `dotnet build benchmarks/Moongazing.OrionGuard.Benchmarks/Moongazing.OrionGuard.Benchmarks.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add benchmarks/Moongazing.OrionGuard.Benchmarks/DomainPrimitivesBenchmark.cs \
        benchmarks/Moongazing.OrionGuard.Benchmarks/Program.cs
git commit -m "perf(benchmarks): add DDD primitives benchmarks (VO equality, aggregate events)"
```

---

## Task 19: Full test suite + solution build verification

**Files:** none modified — verification only.

- [ ] **Step 1: Run the entire test suite**

Run: `dotnet test Moongazing.OrionGuard.sln`
Expected: PASS — all existing tests still green, plus new domain tests (Entity, Aggregate, ValueObject, StronglyTypedId, StronglyTypedIdGuard, DomainLocalization, StronglyTypedIdServiceExtensions) and generator tests (StronglyTypedIdGenerator).

- [ ] **Step 2: Run a clean build for all TFMs**

Run: `dotnet build Moongazing.OrionGuard.sln --no-incremental -c Release`
Expected: 0 errors, 0 warnings (the core csproj has `TreatWarningsAsErrors=true`).

- [ ] **Step 3: If any build warnings surface, fix them in-place**

Typical causes to look for: missing XML docs on new public APIs (project has `GenerateDocumentationFile=true`), nullable annotation mismatches. Fix inline and re-run Step 2 until clean.

- [ ] **Step 4: Commit any follow-up doc/nullable fixes (if any)**

```bash
git add -A
git commit -m "docs(domain): XML doc coverage for v6.1.0 public surface"
```

Skip this commit if no changes were required.

---

## Task 20: Version bump + CHANGELOG + README section

**Files:**
- Modify: `src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj`
- Modify: `src/Moongazing.OrionGuard.Generators/Moongazing.OrionGuard.Generators.csproj`
- Modify: `CHANGELOG.md`
- Modify: `README.md`
- Modify: `docs/FEATURES-v6.md`

- [ ] **Step 1: Bump `Moongazing.OrionGuard.csproj` version**

In `src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj`, change `<Version>6.0.0</Version>` to `<Version>6.1.0</Version>`.

Prepend the following to the `<PackageReleaseNotes>` element (keep prior v6.0.0/v5.0.1 notes below):

```
v6.1.0 — Release Notes

NEW: DDD Domain Primitives — ValueObject (hybrid: abstract base + IValueObject marker for records), Entity<TId> with identity equality and CheckRule / CheckRuleAsync helpers, AggregateRoot<TId> with IAggregateRoot marker and PullDomainEvents() dispatching buffer.

NEW: StronglyTypedId<TValue> — abstract record base for manual ids, plus [StronglyTypedId<TValue>] source generator (Moongazing.OrionGuard.Generators) that emits the partial struct body, EF Core ValueConverter, System.Text.Json JsonConverter, and TypeConverter for supported value types: Guid, int, long, string, Ulid (net9.0+).

NEW: Guard.Against.DefaultStronglyTypedId — throws when a StronglyTypedId is null or wraps the default of its underlying type.

NEW: services.AddOrionGuardStronglyTypedIds() — scans assemblies for generated EF Core converters and registers them.

NEW: IDomainEvent, IBusinessRule, IAsyncBusinessRule, BusinessRuleValidationException, DomainInvariantException — abstractions landing in v6.1.0 so primitives are immediately usable; full base classes and dispatcher arrive in v6.2.0 and v6.3.0.

NEW: Localization — DefaultStronglyTypedId, BusinessRuleBroken, DomainInvariantViolated keys added for all 14 bundled languages.

Full changelog: https://github.com/Moongazing/OrionGuard/blob/master/CHANGELOG.md
```

- [ ] **Step 2: Bump `Moongazing.OrionGuard.Generators.csproj` version**

Change `<Version>6.0.0</Version>` to `<Version>6.1.0</Version>`.

Update `<Description>` to:
```
Source generator for OrionGuard. Generates [GenerateValidator] validators, [StronglyTypedId<TValue>] strongly-typed id types (EF Core ValueConverter, System.Text.Json JsonConverter, TypeConverter companions), and supports NativeAOT-compatible reflection-free workflows.
```

- [ ] **Step 3: Update `CHANGELOG.md`**

Prepend a new section above the current top entry:

````markdown
## v6.1.0 — YYYY-MM-DD

### Added
- `Moongazing.OrionGuard.Domain.Primitives.ValueObject` abstract base class with component-wise equality.
- `Moongazing.OrionGuard.Domain.Primitives.IValueObject` marker for record-based value objects.
- `Moongazing.OrionGuard.Domain.Primitives.Entity<TId>` with identity equality and protected `CheckRule` / `CheckRuleAsync` helpers.
- `Moongazing.OrionGuard.Domain.Primitives.IAggregateRoot` non-generic marker and `AggregateRoot<TId>` with `RaiseEvent` / `PullDomainEvents`.
- `Moongazing.OrionGuard.Domain.Primitives.StronglyTypedId<TValue>` abstract record.
- `[StronglyTypedId<TValue>]` incremental source generator in `Moongazing.OrionGuard.Generators` — emits partial struct body, EF Core `ValueConverter`, `System.Text.Json` converter, and `TypeConverter` for `Guid`, `int`, `long`, `string`, and `Ulid` (net9.0+).
- `Guard.Against.DefaultStronglyTypedId(id, name)` extension.
- `services.AddOrionGuardStronglyTypedIds(params Assembly[])` — registers generated EF Core converters by convention.
- Interfaces shipped for use by v6.2 / v6.3: `IDomainEvent`, `IBusinessRule`, `IAsyncBusinessRule`, exceptions `BusinessRuleValidationException`, `DomainInvariantException`.
- 14-language localization keys: `DefaultStronglyTypedId`, `BusinessRuleBroken`, `DomainInvariantViolated`.
````

Replace `YYYY-MM-DD` with today's ISO date.

- [ ] **Step 4: Update `README.md`**

Insert a new section immediately after the "Dynamic Rule Engine" block and before "RuleSets":

````markdown
### DDD Primitives (NEW in v6.1)

```csharp
// Hybrid ValueObject — abstract class or record-based marker
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public Currency Currency { get; }

    public Money(decimal amount, Currency currency)
    {
        Ensure.That(amount).GreaterThanOrEqualTo(0);
        Amount = amount; Currency = currency;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount; yield return Currency;
    }
}

// Strongly-typed id via source generator
[StronglyTypedId<Guid>]
public readonly partial struct OrderId;

// Aggregate root with domain events
public sealed class Order : AggregateRoot<OrderId>
{
    public Order(OrderId id) : base(id)
    {
        Guard.Against.DefaultStronglyTypedId(id);
    }

    public void Ship()
    {
        CheckRule(new OrderMustBePaidRule(this));
        RaiseEvent(new OrderShippedEvent(Id));
    }
}

// Wire up DI
services.AddOrionGuardStronglyTypedIds();
```

v6.2.0 adds the `IDomainEventDispatcher` and MediatR bridge. v6.3.0 adds the `BusinessRule` base class, `Guard.Against.BrokenRule`, and ASP.NET Core ProblemDetails integration.
````

- [ ] **Step 5: Update `docs/FEATURES-v6.md`**

Inspect existing structure and append a v6.1.0 section mirroring the existing v6.0 sections (headings for "Domain Primitives", "StronglyTypedId source generator", "Localization", "DI"). Copy the bulleted list from CHANGELOG Step 3 as the body.

- [ ] **Step 6: Build + test once more**

Run:
```bash
dotnet build Moongazing.OrionGuard.sln -c Release
dotnet test Moongazing.OrionGuard.sln
```
Expected: both succeed.

- [ ] **Step 7: Commit**

```bash
git add src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj \
        src/Moongazing.OrionGuard.Generators/Moongazing.OrionGuard.Generators.csproj \
        CHANGELOG.md \
        README.md \
        docs/FEATURES-v6.md
git commit -m "release: bump to v6.1.0 with DDD primitives CHANGELOG and README"
```

---

## Self-Review Checklist

Before execution, the engineer (or subagent dispatcher) should confirm:

- [ ] Every public type named in the spec (sections 3.1–3.5) has a corresponding task.
- [ ] Every spec test listed in section 7.1 maps to at least one task:
  - `ValueObjectTests` → Tasks 1, 2
  - `EntityTests` → Tasks 6, 7
  - `AggregateRootTests` → Task 8
  - `StronglyTypedIdTests` → Task 9
- [ ] The `BusinessRuleValidationException` localization resolution (Task 5) uses only public `ValidationMessages.Get`, not any private method.
- [ ] `Entity<TId>.CheckRule` and `CheckRuleAsync` are `protected static` (Task 6) — cannot be called from outside the aggregate.
- [ ] `AggregateRoot.PullDomainEvents` clears the buffer (Task 8 test asserts `Empty(DomainEvents)` after pull).
- [ ] Source generator emits all 4 companion artifacts: partial body (Task 13), EF Core converter (Task 14), JSON converter (Task 15), TypeConverter (Task 16). No artifact is silently skipped.
- [ ] `Ulid` support is guarded by `#if NET9_0_OR_GREATER` in every emitter that references it.
- [ ] 14 localization entries exist for every new key (Task 10 theory has 14 `[InlineData]` rows).
- [ ] Version bump and changelog cover every behavior added (Task 20).
