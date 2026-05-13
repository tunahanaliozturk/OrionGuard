# OrionGuard v6.3.0 — Domain Events, Outbox, OpenTelemetry & Testing

**Date:** 2026-05-09
**Status:** Approved (design); pending implementation plan
**Branch:** `feature/v6.3.0-domain-events`
**Predecessor:** v6.2.0 (`IStronglyTypedId<TValue>`, `DomainEventBase`, `IParsable<TSelf>` strongly-typed ids)

## 1. Goal

Ship the domain-event dispatching layer promised in the v6.2.0 roadmap, plus three production-grade value-adds:

- **Roadmap (mandatory):** `IDomainEventDispatcher`, MediatR bridge, EF Core `SaveChanges` interceptor.
- **Value-adds:** Transactional outbox pattern, OpenTelemetry instrumentation, framework-agnostic test helpers.

## 2. Package layout

| # | Component | Package | Path |
|---|---|---|---|
| 1 | `IDomainEventDispatcher` + default impl | `OrionGuard` (core) | `Domain/Events/` |
| 2 | MediatR bridge | `OrionGuard.MediatR` | `DomainEvents/` (new) |
| 3 | EF Core `SaveChanges` interceptor | `OrionGuard.EntityFrameworkCore` (new) | root |
| 4 | Outbox pattern (`IDomainEventOutbox`, EF Core impl, hosted worker) | `OrionGuard.EntityFrameworkCore` | `Outbox/` |
| 5 | OpenTelemetry instrumentation (`InstrumentedDomainEventDispatcher`) | `OrionGuard.OpenTelemetry` | adds to existing |
| 6 | Test helpers (`DomainEventCapture`, `InMemoryDomainEventDispatcher`) | `OrionGuard.Testing` (new) | root |

**New NuGet PackageIds:** `OrionGuard.EntityFrameworkCore`, `OrionGuard.Testing`.

**Why two new packages instead of folding into core:**
- EF Core kept out of core to preserve console-app / Blazor WASM tenants (same discipline as the v6.2.0 conditional EF Core converter emission for strongly-typed ids).
- Testing kept separate so production builds do not pull in test-only assertion code.

## 3. Core dispatcher (`OrionGuard`)

### 3.1 New types

```csharp
namespace Moongazing.OrionGuard.Domain.Events;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
    Task DispatchAsync(IDomainEvent @event, CancellationToken ct = default);
}

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

public sealed class DomainEventDispatchOptions
{
    public DispatchMode Mode { get; init; } = DispatchMode.SequentialFailFast;
    public bool ContinueOnHandlerException { get; init; }
}

public enum DispatchMode { SequentialFailFast, SequentialContinueOnError, Parallel }
```

### 3.2 Default dispatcher

`ServiceProviderDomainEventDispatcher` resolves `IEnumerable<IDomainEventHandler<TEvent>>` from `IServiceProvider` and runs them according to `DispatchMode`. No MediatR dependency — MediatR users opt in via `OrionGuard.MediatR`.

### 3.3 Error handling matrix

| Mode | Behaviour |
|---|---|
| `SequentialFailFast` | First handler exception aborts; remaining handlers do not run. |
| `SequentialContinueOnError` | All handlers run; exceptions are collected and rethrown as `AggregateException` at the end. |
| `Parallel` | `Task.WhenAll`; exceptions surface as `AggregateException` per `Task.WhenAll` semantics. |

### 3.4 DI helpers

```csharp
services.AddOrionGuardDomainEvents(o => o.Mode = DispatchMode.SequentialFailFast);
services.AddOrionGuardDomainEventHandlers(typeof(Program).Assembly);
```

`AddOrionGuardDomainEventHandlers` scans for `IDomainEventHandler<>` implementations and registers each as `Scoped`.

### 3.5 `AggregateRoot<TId>` — no changes

`RaiseEvent` and `PullDomainEvents` stay as in v6.2.0. The dispatcher pulls events externally; the aggregate has no dispatcher dependency. **This is a load-bearing constraint** — it preserves DDD purity and lets `AggregateRoot` be tested without DI.

## 4. MediatR bridge (`OrionGuard.MediatR`)

### 4.1 Decision: marker-based, not wrapper-based

`IDomainEvent` does **not** inherit `MediatR.INotification`. Instead, MediatR users add the marker to their own event records:

```csharp
// MediatR-free:
public sealed record OrderShipped(OrderId Id) : DomainEventBase;

// MediatR-enabled (one-line difference):
public sealed record OrderShipped(OrderId Id) : DomainEventBase, INotification;
```

### 4.2 Dispatcher impl

```csharp
public sealed class MediatRDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IPublisher _publisher;

    public Task DispatchAsync(IDomainEvent e, CancellationToken ct)
    {
        if (e is INotification n) return _publisher.Publish(n, ct);
        throw new InvalidOperationException(
            $"{e.GetType().Name} must implement MediatR.INotification to use MediatRDomainEventDispatcher.");
    }

    // batch overload calls per-event in sequence
}
```

### 4.3 Why not wrapper

- Consumers write natural `INotificationHandler<OrderShipped>`, not `INotificationHandler<DomainEventNotification<OrderShipped>>`.
- MediatR pipeline behaviours (logging, retry, transaction) compose naturally.
- Zero reflection on the hot path.
- Zero-cost for MediatR-free consumers — `IDomainEvent` does not know `INotification`.

## 5. EF Core `SaveChanges` interceptor (`OrionGuard.EntityFrameworkCore`)

### 5.1 Two modes, one consumer API

| Mode | Behaviour | When to use |
|---|---|---|
| **Inline** (default) | After successful `SaveChangesAsync`, the interceptor pulls events from tracked aggregates and calls `IDomainEventDispatcher` directly. | Single-process apps, tests, demos. |
| **Outbox** (opt-in) | Interceptor writes events as `OutboxMessage` rows in the same transaction; a hosted worker dispatches asynchronously. | Production with a message broker, multi-process delivery, at-least-once guarantees. |

The choice is a single options call:

```csharp
services.AddOrionGuardEfCore<AppDbContext>(o => o.UseInline());   // default
services.AddOrionGuardEfCore<AppDbContext>(o => o.UseOutbox());   // production
```

Consumer code (aggregate, handlers, `RaiseEvent`) is identical in both modes.

### 5.2 Pull-once contract

`AggregateRoot.PullDomainEvents()` empties the buffer. The interceptor must call it exactly once per aggregate per save:

```csharp
foreach (var entry in ctx.ChangeTracker.Entries<IAggregateRoot>())
{
    var events = entry.Entity.PullDomainEvents();   // empties the buffer
    if (events.Count == 0) continue;
    // inline: queue for post-save dispatch
    // outbox: serialize + add OutboxMessage to ctx
}
```

### 5.3 Inline mode lifecycle

`SavingChangesAsync` collects events to a transient per-`DbContext` list; `SavedChangesAsync` (post-commit) iterates the list and calls `IDomainEventDispatcher`. If dispatch throws, the DB is already committed — `Inline` mode trades durability for simplicity. Production users pick `UseOutbox()`.

### 5.4 Behaviour on save failure

`PullDomainEvents()` empties the in-memory buffer at `SavingChangesAsync` time. If the subsequent commit fails:

- **Inline mode:** Events are lost; the aggregate's in-memory state diverges from DB state. Documented contract: *the caller must discard the aggregate instance after a save failure* (do not reuse it). Same as standard EF Core practice for any tracked entity.
- **Outbox mode:** Outbox rows roll back atomically with aggregate state. The aggregate buffer is still empty, but no events have been "fired" anywhere observable — discarding the aggregate is correct and lossless from the system's perspective.

This behaviour is identical to the canonical Vladimir Khorikov / Microsoft eShopOnContainers pattern; it is not a regression introduced by OrionGuard.

## 6. Outbox pattern (`OrionGuard.EntityFrameworkCore`)

### 6.1 `OutboxMessage` entity

```csharp
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; }       // assembly-qualified
    public string Payload { get; set; }         // System.Text.Json
    public DateTime OccurredOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public string? CorrelationId { get; set; }
    public string? TraceParent { get; set; }    // W3C trace context
    public string? TraceState { get; set; }
}
```

Index: `(ProcessedOnUtc, OccurredOnUtc)` for unprocessed lookup.

### 6.2 Table location

Outbox table lives in **the consumer's `DbContext`**, not a dedicated outbox `DbContext`. Rationale: the `OutboxMessage` insert and the aggregate state changes commit in the same transaction. A separate `DbContext` would lose this guarantee.

### 6.3 Worker — `OutboxDispatcherHostedService`

`BackgroundService` polling loop:

```
loop until cancelled:
    create scope
    take batch of unprocessed messages (ordered by OccurredOnUtc, BatchSize=100 default)
    for each msg:
        try:
            deserialize via Type.GetType(msg.EventType)
            dispatcher.DispatchAsync(event)
            msg.ProcessedOnUtc = utcnow
        catch ex:
            msg.RetryCount++
            msg.Error = ex.ToString()
            if msg.RetryCount >= MaxRetries: msg.ProcessedOnUtc = utcnow  // dead-letter
    SaveChangesAsync
    await Task.Delay(PollingInterval)   // 5s default
```

### 6.4 `OutboxOptions`

```csharp
public sealed class OutboxOptions
{
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; init; } = 100;
    public int MaxRetries { get; init; } = 5;
    public string TableName { get; init; } = "OrionGuard_Outbox";
}
```

### 6.5 Out of scope (deferred to v6.4+)

- Distributed locking for multi-instance workers (v6.3.0 assumes single-instance).
- Outbox archival / cleanup job.
- Push-based dispatch (`LISTEN/NOTIFY` for Postgres, `SqlDependency` for SQL Server).
- `OutboxTypeMapRegistry` (alias system for safe type renames).
- Cross-`DbContext` outbox.

## 7. OpenTelemetry instrumentation (`OrionGuard.OpenTelemetry`)

### 7.1 Sources & meters

```
ActivitySource: Moongazing.OrionGuard.DomainEvents
Meter:          Moongazing.OrionGuard.DomainEvents
```

Metrics:
- `orionguard.domain_events.dispatched` (counter)
- `orionguard.domain_events.failed` (counter)
- `orionguard.domain_events.duration` (histogram, ms)
- `orionguard.outbox.processed` (counter)
- `orionguard.outbox.retries` (counter)

Span name format: `DomainEvent.Dispatch {EventTypeName}`. Tags: `orionguard.event.id`, `orionguard.event.type`, `orionguard.event.occurred_on`. **No payload data** — PII safety.

### 7.2 `InstrumentedDomainEventDispatcher` — decorator

Wraps any `IDomainEventDispatcher`, opens a span, records duration / counters, sets activity status on exception.

### 7.3 Distributed trace context propagation

Outbox mode breaks the span chain (web request → DB → background worker). To preserve a single trace:

- Interceptor captures `Activity.Current?.Id` and `Activity.Current?.TraceStateString` and writes them onto `OutboxMessage`.
- Worker calls `ActivityContext.Parse(traceParent, traceState)` and starts the dispatch activity with that parent.

Result: Jaeger / Tempo show `web request → DB save → outbox dispatch → handler` as one trace.

### 7.4 DI helper

```csharp
services.AddOpenTelemetry()
    .AddOrionGuardDomainEvents()              // registers ActivitySource + Meter
    .WithTracing(t => t.AddOtlpExporter())
    .WithMetrics(m => m.AddOtlpExporter());

services.AddOrionGuardDomainEvents()
    .WithOpenTelemetry();                     // wraps dispatcher with InstrumentedDomainEventDispatcher
```

### 7.5 .NET 8 compatibility

`Activity.AddException` is .NET 9+. For net8.0, fall back to `activity.AddEvent(new ActivityEvent("exception", tags: ...))`.

## 8. Test helpers (`OrionGuard.Testing`)

### 8.1 `DomainEventCapture` — aggregate-level

```csharp
var order = new Order(OrderId.New());
order.Ship();

var events = DomainEventCapture.From(order);
events.Should().HaveRaised<OrderShipped>(e => e.OrderId == order.Id);
events.Should().NotHaveRaised<OrderCancelled>();
events.Should().HaveRaisedExactly(1).Of<OrderShipped>();
```

### 8.2 `InMemoryDomainEventDispatcher` — integration-level

```csharp
var dispatcher = new InMemoryDomainEventDispatcher();
factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
    s.Replace(ServiceDescriptor.Scoped<IDomainEventDispatcher>(_ => dispatcher))));

await client.PostAsync("/orders/1/ship", null);

dispatcher.Should().HaveRaised<OrderShipped>(e => e.OrderId == new OrderId(1));
```

### 8.3 Test framework agnostic

`DomainEventAssertionException` is the only assertion failure type. No dependency on xUnit / NUnit / MSTest / FluentAssertions / Shouldly. Works across all runners.

**Why no FluentAssertions:** v8+ ships under a commercial licence; bundling it would either break our MIT story or force a dual-licence dance. A minimal in-house DSL is the right tradeoff.

### 8.4 Out of scope

- `BusinessRule` test helpers (deferred to v6.4 alongside the `BusinessRule` base class).
- Snapshot testing.
- Property-based testing integration.
- Event sourcing fixtures.

## 9. Public API summary

### 9.1 New types in `OrionGuard` core

```
Moongazing.OrionGuard.Domain.Events.IDomainEventDispatcher
Moongazing.OrionGuard.Domain.Events.IDomainEventHandler<TEvent>
Moongazing.OrionGuard.Domain.Events.DomainEventDispatchOptions
Moongazing.OrionGuard.Domain.Events.DispatchMode
Moongazing.OrionGuard.Domain.Events.ServiceProviderDomainEventDispatcher
Moongazing.OrionGuard.DependencyInjection.AddOrionGuardDomainEvents
Moongazing.OrionGuard.DependencyInjection.AddOrionGuardDomainEventHandlers
```

### 9.2 New types in `OrionGuard.MediatR`

```
Moongazing.OrionGuard.MediatR.DomainEvents.MediatRDomainEventDispatcher
Moongazing.OrionGuard.MediatR.DomainEvents.AddOrionGuardMediatRDomainEvents
```

### 9.3 New package — `OrionGuard.EntityFrameworkCore`

```
Moongazing.OrionGuard.EntityFrameworkCore.DomainEventSaveChangesInterceptor
Moongazing.OrionGuard.EntityFrameworkCore.OrionGuardEfCoreOptions
Moongazing.OrionGuard.EntityFrameworkCore.AddOrionGuardEfCore
Moongazing.OrionGuard.EntityFrameworkCore.Outbox.OutboxMessage
Moongazing.OrionGuard.EntityFrameworkCore.Outbox.OutboxOptions
Moongazing.OrionGuard.EntityFrameworkCore.Outbox.OutboxDispatcherHostedService
```

### 9.4 New types in `OrionGuard.OpenTelemetry`

```
Moongazing.OrionGuard.OpenTelemetry.DomainEvents.OrionGuardDomainEventTelemetry
Moongazing.OrionGuard.OpenTelemetry.DomainEvents.InstrumentedDomainEventDispatcher
Moongazing.OrionGuard.OpenTelemetry.DomainEvents.AddOrionGuardDomainEvents
```

### 9.5 New package — `OrionGuard.Testing`

```
Moongazing.OrionGuard.Testing.DomainEvents.DomainEventCapture
Moongazing.OrionGuard.Testing.DomainEvents.DomainEventAssertions
Moongazing.OrionGuard.Testing.DomainEvents.InMemoryDomainEventDispatcher
Moongazing.OrionGuard.Testing.DomainEvents.DomainEventAssertionException
```

## 10. Migration notes

- **Source-compatible with v6.2.0.** No breaking changes.
- Consumers already raising events via `RaiseEvent` continue to work; events simply do not dispatch unless `AddOrionGuardDomainEvents` is wired.
- MediatR consumers add `, INotification` to their event records (one-line per event).
- Outbox consumers add an EF Core migration for the `OrionGuard_Outbox` table.

## 11. Validation checklist (definition of done)

- [ ] All new types covered by unit tests.
- [ ] EF Core interceptor covered by integration tests against SQLite + InMemory providers.
- [ ] Outbox worker covered by integration tests with cancellation, retry, dead-letter scenarios.
- [ ] Distributed-trace propagation verified end-to-end (web → DB → outbox → handler) via OpenTelemetry test exporter.
- [ ] Sample app in `demo/` updated showing all five components in action.
- [ ] `CHANGELOG.md` v6.3.0 section drafted.
- [ ] All existing tests still pass.
- [ ] NativeAOT build for `OrionGuard` core still succeeds (no new reflection in core hot paths).
- [ ] All projects target `net8.0;net9.0;net10.0`.

## 12. Roadmap pointers

- **v6.4.0:** `BusinessRule` base class + `Guard.Against.BrokenRule` + ASP.NET Core ProblemDetails mapping (originally the plan); plus distributed locking for multi-instance outbox workers, `OutboxTypeMapRegistry`, archival job.
- **v6.5+:** Push-based outbox dispatch (LISTEN/NOTIFY, SqlDependency), event sourcing primitives.
