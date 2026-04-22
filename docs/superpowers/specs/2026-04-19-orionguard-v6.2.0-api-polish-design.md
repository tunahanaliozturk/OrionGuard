# OrionGuard v6.2.0 — API Polish & Closing the v6.1 Gaps

**Date:** 2026-04-19
**Author:** Tunahan Ali Ozturk
**Status:** Design approved, pending implementation plan
**Target version:** v6.2.0 (SemVer minor — new public API, zero breaking changes for v6.1.0 consumers)

---

## 1. Motivation

v6.1.0 shipped the first-phase DDD primitives (`ValueObject`, `Entity<TId>`, `AggregateRoot<TId>`, `StronglyTypedId<TValue>` base record, source generator) plus guard/DI/localization wiring. Building the demo against every feature surfaced four practical gaps that deserve a dedicated minor release:

- The generator unconditionally emits an EF Core `ValueConverter` companion even when the consumer project does not reference EF Core, causing a compile failure; the demo had to pull in a spurious `Microsoft.EntityFrameworkCore` dependency just to make the build succeed.
- Source-generated struct IDs (`[StronglyTypedId<T>] readonly partial struct`) do not inherit from `StronglyTypedId<TValue>`, so they cannot use the `AgainstDefaultStronglyTypedId` guard — users have two ID styles with different guard ergonomics.
- `IDomainEvent` is an interface only; every concrete domain event has to hand-roll `Guid EventId` and `DateTime OccurredOnUtc`, which is friction the `DomainEventBase` record (originally slotted for v6.2) would eliminate without requiring the full event-dispatcher stack.
- Generated struct IDs do not implement `IParsable<TSelf>` / `ISpanParsable<TSelf>`, so ASP.NET Core minimal APIs cannot bind them from route / query / form parameters out of the box.

These four items are tightly coupled ergonomic improvements that make the v6.1 API finally feel consistent. Shipping them as v6.2.0 clears the "known gaps" log before the bigger v6.3.0 work on domain-event dispatching.

**Roadmap shift:** The original v6.2.0 scope (domain-event dispatcher + MediatR bridge + EF Core interceptor) becomes v6.3.0. The original v6.3.0 scope (full business rule base class + `Guard.Against.BrokenRule` + AspNetCore `ProblemDetails` mapping) becomes v6.4.0.

Non-goals (deferred, NOT in scope):

- `IDomainEventDispatcher`, MediatR bridge, EF Core `SaveChanges` interceptor → v6.3.0.
- `BusinessRule` / `AsyncBusinessRule` abstract base classes, `Guard.Against.BrokenRule`, `Validate.Rule` / `Validate.Rules` → v6.4.0.
- `[StronglyTypedId<TValue>]` support for custom conversion strategies (e.g., `ValueConverter` factory) → revisit on demand.
- Entity `IsTransient()` helper — nice-to-have but not part of v6.2.0 scope; revisit if demand arises.
- Generator snapshot tests via `Verify.Xunit` — defer to v6.3.0 tooling pass.

---

## 2. Feature A — Conditional EF Core converter emission

### 2.1 Problem

`StronglyTypedIdGenerator.RegisterSourceOutput` always calls `EfCoreConverterEmitter.Emit(...)`, producing a `*EfCoreValueConverter.g.cs` file that references `global::Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<,>`. Consumer projects without EF Core fail to compile with `CS0234: namespace 'EntityFrameworkCore' does not exist`.

### 2.2 Design

Detect EF Core availability via `Compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter`2")` inside the generator. When the type is unresolved, skip emitting the EF Core converter for that compilation.

Implementation shape:

- `StronglyTypedIdGenerator.Initialize` gains a `CompilationProvider` branch that projects a single `bool hasEfCore` down the pipeline alongside the existing target stream.
- `targets.Combine(hasEfCoreProvider)` passes the flag to `RegisterSourceOutput`.
- The callback emits `EfCoreConverterEmitter.*` only when `hasEfCore` is `true`; JSON and TypeConverter emitters always run (those types are in the BCL).
- No other observable behavior changes. If EF Core is later added to the project, the next incremental build will resume emitting the converter.

### 2.3 Consumer impact

- Console apps, class libraries, and Blazor WASM projects that never touch EF Core stop failing on the generated converter file. The demo can drop its `Microsoft.EntityFrameworkCore` PackageReference.
- `services.AddOrionGuardStronglyTypedIds(...)` continues to work — when no converters are emitted, the scan finds nothing, which is correct.
- Existing EF Core consumers see no change.

---

## 3. Feature B — `IStronglyTypedId<TValue>` marker interface

### 3.1 Problem

`AgainstDefaultStronglyTypedId<TValue>(this StronglyTypedId<TValue> id, ...)` only accepts references to the abstract record. Source-generated structs are not part of the record hierarchy, so users must hand-roll a default check for generated IDs.

### 3.2 Design

Introduce a minimal marker interface in `Moongazing.OrionGuard.Domain.Primitives`:

```csharp
public interface IStronglyTypedId<TValue>
    where TValue : notnull, IEquatable<TValue>
{
    TValue Value { get; }
}
```

Wiring:

- `public abstract record StronglyTypedId<TValue>(TValue Value) : IStronglyTypedId<TValue> where TValue : notnull, IEquatable<TValue>` — the existing abstract record now implements the new interface. No code change to the record body (it already exposes `Value`); only the declaration line gains the interface.
- Source generator appends `: global::Moongazing.OrionGuard.Domain.Primitives.IStronglyTypedId<{valueCsKeyword}>` to the generated struct body. `IEquatable<TSelf>` stays; the interface list grows.
- `AgainstDefaultStronglyTypedId` receiver changes from `this StronglyTypedId<TValue> id` to `this IStronglyTypedId<TValue> id`, and the return type from `StronglyTypedId<TValue>` to `IStronglyTypedId<TValue>`.
- Method body is unchanged: `id is null` check, then `EqualityComparer<TValue>.Default.Equals(id.Value, default!) || (id.Value is string s && string.IsNullOrEmpty(s))`.

### 3.3 Breaking-change analysis

- Binary compatibility: receiver and return type changes to the extension method are API-breaking at the binary level — anyone who compiled against v6.1.0's extension signature would need a recompile. Source compatibility is preserved because the old manual record still implements the new interface.
- v6.1.0 consumers doing `orderId.AgainstDefaultStronglyTypedId(...)` on a manual record continue to compile without change; the result type narrows from `StronglyTypedId<Guid>` to `IStronglyTypedId<Guid>`, which remains assignable to existing `var`-typed locals.
- Because v6.1.0 has been publicly tagged, this is documented as a "recompile recommended" note in the CHANGELOG — no user code change required.

---

## 4. Feature C — `DomainEventBase` abstract record

### 4.1 Problem

Every concrete domain event reproduces the same two properties:

```csharp
public sealed record OrderPlacedEvent(OrderId Id) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
```

### 4.2 Design

Add one abstract record to `Moongazing.OrionGuard.Domain.Events`:

```csharp
public abstract record DomainEventBase : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
```

`init` accessors allow test code to fix both values via `with { EventId = fixedId, OccurredOnUtc = fixedTimestamp }` without introducing a second constructor overload.

Consumer becomes:

```csharp
public sealed record OrderPlacedEvent(OrderId Id) : DomainEventBase;
```

- No dispatcher dependency; v6.3.0 will add the dispatcher independently of this type.
- `AggregateRoot<TId>.RaiseEvent(IDomainEvent)` continues to work unchanged because `DomainEventBase : IDomainEvent`.

---

## 5. Feature D — `IParsable<TSelf>` / `ISpanParsable<TSelf>` on generated IDs

### 5.1 Problem

ASP.NET Core minimal API route/query binding uses `IParsable<T>.TryParse` (and `ISpanParsable<T>.TryParse` for hot paths) to convert strings to parameter types. Generated struct IDs currently lack both, so `app.MapGet("/orders/{id:OrderId}", (OrderId id) => ...)` does not bind without a custom `TypeConverter` hop.

### 5.2 Design

Source generator emits the following members on every generated struct:

```csharp
public static TSelf Parse(string s, global::System.IFormatProvider? provider)
    => new TSelf({inner-parse});

public static bool TryParse(string? s, global::System.IFormatProvider? provider, out TSelf result)
{
    if ({inner-try-parse}) { result = new TSelf(v); return true; }
    result = default;
    return false;
}

public static TSelf Parse(global::System.ReadOnlySpan<char> s, global::System.IFormatProvider? provider)
    => new TSelf({inner-span-parse});

public static bool TryParse(global::System.ReadOnlySpan<char> s, global::System.IFormatProvider? provider, out TSelf result)
{
    if ({inner-try-span-parse}) { result = new TSelf(v); return true; }
    result = default;
    return false;
}
```

Interface list on the struct gains `global::System.IParsable<TSelf>`, `global::System.ISpanParsable<TSelf>`.

Per-type `{inner-*}` expansions:

| Value type | Parse | TryParse | Span Parse | Span TryParse |
|------------|-------|----------|------------|---------------|
| `Guid`     | `Guid.Parse(s, provider)` | `Guid.TryParse(s, provider, out var v)` | `Guid.Parse(s, provider)` | `Guid.TryParse(s, provider, out var v)` |
| `int`      | `int.Parse(s, provider)` | `int.TryParse(s, global::System.Globalization.NumberStyles.Integer, provider, out var v)` | `int.Parse(s, global::System.Globalization.NumberStyles.Integer, provider)` | `int.TryParse(s, global::System.Globalization.NumberStyles.Integer, provider, out var v)` |
| `long`     | `long.Parse(s, provider)` | `long.TryParse(s, global::System.Globalization.NumberStyles.Integer, provider, out var v)` | `long.Parse(s, global::System.Globalization.NumberStyles.Integer, provider)` | `long.TryParse(s, global::System.Globalization.NumberStyles.Integer, provider, out var v)` |
| `string`   | `s ?? throw new global::System.ArgumentNullException(nameof(s))` | `s is not null` → `v = s` | `s.ToString()` | always `true`, `v = s.ToString()` |
| `Ulid`     | `global::System.Ulid.Parse(s, provider)` | `global::System.Ulid.TryParse(s, provider, out var v)` | `global::System.Ulid.Parse(s, provider)` | `global::System.Ulid.TryParse(s, provider, out var v)` |

The `Ulid` rows are wrapped in `#if NET9_0_OR_GREATER` (same guard the existing `StronglyTypedIdEmitter` uses for `Ulid.NewUlid()` / `Empty`).

### 5.3 Error semantics

Failure semantics follow standard .NET: `Parse` throws `FormatException` (propagated from the inner primitive's `Parse`), `TryParse` returns `false`. No OrionGuard-specific exception. This preserves interop with ASP.NET Core minimal API route binding, which catches `FormatException` and returns 400 Bad Request automatically.

### 5.4 TFM compatibility

`IParsable<T>` / `ISpanParsable<T>` were added in .NET 7. The OrionGuard core package targets `net8.0;net9.0;net10.0`, so any consumer project that references a generated struct already has both interfaces available. No `#if` guard is required around `IParsable<T>` itself — the `Ulid` rows use `#if NET9_0_OR_GREATER` for a separate reason.

---

## 6. Testing strategy

Unit tests land in the existing test projects; no new test project is created.

### 6.1 `tests/Moongazing.OrionGuard.Tests/`

- `IStronglyTypedIdInterfaceTests.cs` — asserts `StronglyTypedId<T>` (manual record) is assignable to `IStronglyTypedId<T>`; asserts `Value` property is accessible via the interface.
- `StronglyTypedIdGuardTests.cs` — extend existing file with a test confirming `AgainstDefaultStronglyTypedId` works when the receiver is typed as `IStronglyTypedId<Guid>` rather than the concrete record.
- `DomainEventBaseTests.cs` — asserts `EventId` and `OccurredOnUtc` default to non-empty Guid / current UTC; `with` expression permits override; instance satisfies `IDomainEvent`.

### 6.2 `tests/Moongazing.OrionGuard.Generators.Tests/`

- New `StronglyTypedIdGeneratorConditionalEfCoreTests.cs` — runs the generator against a compilation that does NOT reference EF Core, asserts no `*EfCoreValueConverter.g.cs` file is produced; and against a compilation that DOES reference EF Core, asserts the converter is produced as before.
- `StronglyTypedIdGeneratorTests.cs` — extend to assert the generated struct declares `IStronglyTypedId<TValue>` in its interface list.
- `StronglyTypedIdGeneratorParsableTests.cs` (new) — asserts generated struct declares `IParsable<TSelf>` and `ISpanParsable<TSelf>`; asserts emitted text contains `public static TSelf Parse(`, `public static bool TryParse(`, and the per-type inner parse call (e.g., `Guid.TryParse`).

### 6.3 Demo regression

The `Moongazing.OrionGuard.Demo` console app drops its `Microsoft.EntityFrameworkCore` package reference; run it to confirm it still builds and all 18 sections still print. Expected count of registered EF Core converters changes to 0 (no EF Core in the build), which the demo section 17 prints truthfully.

### 6.4 Total additions

- ~3 new test files, ~1 existing test file extended.
- ~25 new test cases.
- All prior 506 tests must continue to pass (no regressions).

---

## 7. Rollout and release notes

### 7.1 Version bumps (ecosystem lockstep)

All nine packages bump from `6.1.0` to `6.2.0`:

- `OrionGuard` (core)
- `Moongazing.OrionGuard.Generators`
- `Moongazing.OrionGuard.AspNetCore`
- `Moongazing.OrionGuard.Blazor`
- `Moongazing.OrionGuard.Grpc`
- `Moongazing.OrionGuard.MediatR`
- `Moongazing.OrionGuard.OpenTelemetry`
- `Moongazing.OrionGuard.SignalR`
- `Moongazing.OrionGuard.Swagger`

### 7.2 Documentation updates

- `CHANGELOG.md` gains a v6.2.0 entry listing the four features + migration note on `IStronglyTypedId<TValue>`.
- `README.md` gains a short note under the v6.1 "DDD Primitives" block referencing the unified guard + minimal-API parseability.
- `docs/FEATURES-v6.1.md` — append a "v6.2 additions" section; do not create a new `FEATURES-v6.2.md` because the feature set is an extension of the same DDD toolkit.
- Core csproj `PackageReleaseNotes` gains a v6.2.0 block above the v6.1.0 block.
- `docs/ROADMAP.md` updated: v6.2 → v6.3, v6.3 → v6.4, and the "v6.2 = API polish" line added.

### 7.3 Migration guidance for v6.1.0 users

- No source code change required. Recompile against v6.2.0 binaries and everything continues to work.
- New ergonomics: source-generated struct IDs can now call `.AgainstDefaultStronglyTypedId(...)`; domain events can `: DomainEventBase` for free `EventId`/`OccurredOnUtc`; minimal API endpoints bind `OrderId` parameters without a custom route constraint.

---

## 8. File-by-file summary

**New files:**

| Path | Purpose |
|------|---------|
| `src/Moongazing.OrionGuard/Domain/Primitives/IStronglyTypedId.cs` | Marker interface for unified ID style |
| `src/Moongazing.OrionGuard/Domain/Events/DomainEventBase.cs` | Abstract record with canonical `EventId` + `OccurredOnUtc` |
| `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/ParsableEmitter.cs` | Emits `IParsable`/`ISpanParsable` implementations |
| `tests/Moongazing.OrionGuard.Tests/IStronglyTypedIdInterfaceTests.cs` | Assignability + `Value` access via interface |
| `tests/Moongazing.OrionGuard.Tests/DomainEventBaseTests.cs` | Default init + `with` override + `IDomainEvent` polymorphism |
| `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorConditionalEfCoreTests.cs` | Conditional EF Core emission matrix |
| `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorParsableTests.cs` | `IParsable` / `ISpanParsable` emission |
| `docs/FEATURES-v6.1.md` — append a `## v6.2 Additions` section at the bottom | Feature guide |

**Modified files:**

| Path | Change |
|------|--------|
| `src/Moongazing.OrionGuard/Domain/Primitives/StronglyTypedId.cs` | Declaration adds `: IStronglyTypedId<TValue>` |
| `src/Moongazing.OrionGuard/Extensions/StronglyTypedIdGuards.cs` | Receiver + return type switch to `IStronglyTypedId<TValue>` |
| `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdEmitter.cs` | Interface list appended; new `ParsableEmitter.Emit` call |
| `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs` | `CompilationProvider` detection for EF Core; gated emitter call |
| `tests/Moongazing.OrionGuard.Tests/StronglyTypedIdGuardTests.cs` | Interface-typed receiver test |
| `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs` | Asserts `IStronglyTypedId<TValue>` in interface list |
| `CHANGELOG.md` | v6.2.0 entry |
| `README.md` | v6.2 note |
| `docs/ROADMAP.md` | Shift v6.2/v6.3 slots |
| All 9 `*.csproj` | Version 6.1.0 → 6.2.0 |
| `src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj` | `PackageReleaseNotes` v6.2.0 block |
| `demo/Moongazing.OrionGuard.Demo/Moongazing.OrionGuard.Demo.csproj` | Drop `Microsoft.EntityFrameworkCore` PackageReference |
| `demo/Moongazing.OrionGuard.Demo/Program.cs` | Section 17 message acknowledges 0 converters when EF Core absent |

Total: ~8 new files, ~13 modified files, ~25 new tests, 9 package versions bumped.

---

## 9. Open questions

None at design time. All design choices have been made:

- Conditional EF Core emission uses `Compilation.GetTypeByMetadataName` detection: **approved**.
- `IStronglyTypedId<TValue>` in `Moongazing.OrionGuard.Domain.Primitives`: **approved**.
- `DomainEventBase` record with `init` accessors for test override: **approved**.
- `IParsable<T>` / `ISpanParsable<T>` using standard .NET `FormatException` semantics (option A): **approved**.
- Version 6.2.0, roadmap items shift by one minor: **approved**.
- No new NuGet packages: **approved**.
