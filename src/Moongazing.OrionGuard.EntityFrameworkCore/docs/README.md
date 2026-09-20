# OrionGuard.EntityFrameworkCore

Publishes the domain events your aggregates raise, at the moment the data they describe is committed — inline after the save, or through a transactional outbox that survives the process dying.

```bash
dotnet add package OrionGuard.EntityFrameworkCore
```

```csharp
namespace Shop;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

public sealed record OrderShipped(Guid OrderId) : DomainEventBase;

public sealed class Order(Guid id) : AggregateRoot<Guid>(id)
{
    public void Ship() => RaiseEvent(new OrderShipped(Id));
}

public sealed class OrderShippedHandler : IDomainEventHandler<OrderShipped>
{
    public Task HandleAsync(OrderShipped @event, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Ignore(o => o.DomainEvents);
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration("OrionGuard_Outbox"));
        modelBuilder.ApplyConfiguration(new OutboxLockEntityTypeConfiguration());
    }
}

public static class OutboxSetup
{
    public static void Add(IServiceCollection services, string connectionString)
    {
        // The (sp, options) overload lets the interceptor resolve its collaborators from the
        // context's own scope.
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseSqlServer(connectionString)
            .UseOrionGuardDomainEvents(sp));

        services.AddOrionGuardDomainEvents();
        services.AddOrionGuardDomainEventHandlers(typeof(OrderShippedHandler).Assembly);
        services.AddOrionGuardEfCore<AppDbContext>(o => o.UseOutbox());
    }
}
```

After an EF Core migration for the two tables, `order.Ship()` followed by `SaveChangesAsync()` writes one `OrionGuard_Outbox` row inside the same transaction as the order. A hosted worker picks it up on its next poll, deserializes the event, calls `OrderShippedHandler`, and stamps the row processed. If the process dies between the commit and the dispatch, the row is still there.

The core `OrionGuard` package comes along as a dependency; add the EF Core provider for your database yourself.

## Inline or Outbox

`UseInline()` is the default; `UseOutbox(...)` switches strategy.

| | Inline | Outbox |
| --- | --- | --- |
| When handlers run | Inside `SaveChanges`/`SaveChangesAsync`, after the save succeeds; inside your own `BeginTransaction`, when it commits | In `OutboxDispatcherHostedService`, after the transaction commits |
| Where events live | Nowhere — dispatched from memory | One `OutboxMessage` row per event, written by the same `SaveChanges` as the aggregate |
| A handler throws | The exception leaves `SaveChanges` (or the commit), the data is already saved, and the remaining events of that save are dropped | The row is retried on later polls, then dead-lettered |
| Process dies after the save | Events are lost | Rows stay and are delivered later |
| Delivery | At most once | At least once |

Saving inside your own `Database.BeginTransaction()` holds Inline events until that transaction commits, and drops them if it rolls back or is disposed without a commit; a handler exception then leaves the commit call. A synchronous `SaveChanges()` dispatches on a thread-pool thread and blocks until the handlers finish — the same contract as the async path, but prefer `SaveChangesAsync()` when handlers do I/O. A failed save leaves the events on the aggregates, so a retry picks them up.

`AddOrionGuardDomainEvents(o => o.Mode = ...)` decides how the handlers *of one event* are invoked: `SequentialFailFast` (default), `SequentialContinueOnError` (runs all, then throws `AggregateException`), or `Parallel`.

## How outbox delivery works

Each poll the worker takes the distributed lock, reads up to `BatchSize` unprocessed rows ordered by `OccurredOnUtc`, then resolves, deserializes (System.Text.Json) and dispatches each one through `IDomainEventDispatcher` before stamping `ProcessedOnUtc` — one row at a time. When a full batch drains it polls again immediately, so a backlog is not rate-limited by the polling interval; after a partial, empty or partly-failed batch it waits for the next tick or a wake signal.

- **Failure.** `RetryCount` is incremented and `Error` holds the exception text. Retries come on later polls with no backoff beyond the polling interval. At `MaxRetries` the row is stamped processed and becomes a dead letter, `Error` intact. An `OperationCanceledException` from a handler (an `HttpClient` timeout) is an ordinary failure; only host shutdown stops the worker.
- **Unresolvable rows** are dead-lettered on the first attempt, with an `Error` starting `TYPE_NOT_FOUND`, `TYPE_NOT_DOMAIN_EVENT` or `DESERIALIZE_FAILED`.
- **Scope per row.** Every row is dispatched in a DI scope of its own, so a handler writing through the scoped `DbContext` has those writes committed in one transaction with the row's processed stamp. A handler that throws has its writes discarded; writes that cannot be saved (a constraint violation) are discarded too and the row is recorded as a failed attempt — `Error` starts "The event was dispatched, but saving ..." — which counts towards `MaxRetries`.
- **Concurrent changes.** Every row update, the successful one included, is conditional on the state the row was read in, so a replay or discard made from the [dashboard](https://www.nuget.org/packages/OrionGuard.Outbox.Dashboard) while the handler runs wins: the row stays queued for the replayed delivery and that attempt's handler writes are rolled back rather than applied twice. On a provider without conditional updates (EF Core InMemory) the check and the update are separate steps.
- **Failed polls.** A fault outside any row — database unreachable, missing table or registration — is logged at Error, counted by `orionguard.outbox.dispatcher.batch_faults`, and retried after the polling interval.
- **Failure observer.** Register `IOutboxRowFailureObserver` to hear about every failed attempt, transient or terminal. Observer exceptions are logged and ignored.

### Outbox options

Set through `UseOutbox(o => ...)`. Setters throw on invalid values.

| Option | Default | Notes |
| --- | --- | --- |
| `PollingInterval` | 5 s | Must be > 0. Also the upper bound on wake latency. |
| `BatchSize` | 100 | Rows read per poll. |
| `MaxRetries` | 5 | Failed attempts before a row is dead-lettered. At least 1. |
| `LockKey` | `orion_guard_outbox_dispatcher` | Distributed lock key. |
| `LockLeaseDuration` | 30 s | Must exceed one batch's wall-clock cost. Not renewed. |
| `TableName` | `OrionGuard_Outbox` | Not applied for you — pass the same name to `OutboxMessageEntityTypeConfiguration`. |

## Schema and migrations

`new OutboxMessageEntityTypeConfiguration(tableName, indexFilter = null)` maps `OutboxMessage` and creates `IX_OrionGuard_Outbox_Unprocessed`: a composite index on `(ProcessedOnUtc, OccurredOnUtc)` by default, or a filtered index on `OccurredOnUtc` when you pass a provider-specific filter such as `"[ProcessedOnUtc] IS NULL"` (SQL Server) or `"\"ProcessedOnUtc\" IS NULL"` (PostgreSQL) — the filtered form stays small as processed rows pile up.

`new OutboxLockEntityTypeConfiguration()` maps `OrionGuard_OutboxLocks`, needed only with the default `SkipLockedDistributedLock`.

Generate the migration with `dotnet ef migrations add` as usual; reference DDL per provider is in the [outbox locks migration guide](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/v6.4.0-outbox-locks.md).

## Distributed lock

Outbox mode registers `SkipLockedDistributedLock`: one lease row per key, taken with a conditional `UPDATE`/`INSERT` in a transaction and confirmed by reading the holder back. A replica that loses skips that poll. Losing means the winner's row is there — any *other* `INSERT` failure (a key longer than the column, a constraint, a transient fault) is thrown and logged as a failed poll rather than mistaken for contention. Its SQL takes table, schema and column names from the `OutboxLock` mapping in your model, so a renamed table or a naming convention is honoured, and quotes them the way your provider does, which PostgreSQL needs for the mixed-case `"OrionGuard_OutboxLocks"`; values are always parameters. With `OutboxLock` unmapped or its table missing, the lock logs one warning naming the cause and every poll is skipped, so rows accumulate until the mapping and migration are in place.

- `UseDistributedLock<NullDistributedLock>()` always acquires — for a single instance, with no lock table.
- `UseDistributedLock<TLock>()` plugs in any `IDistributedLock`; [OrionGuard.Locks.Redis](https://www.nuget.org/packages/OrionGuard.Locks.Redis) is the Redis one.
- The archival worker uses the same `IDistributedLock` under its own key.

## Type mapping, and why the fallback is a trade-off

By default a row stores the event's assembly-qualified name and resolves it with `Type.GetType`, so renaming or moving an event type strands older rows. Map stable logical names instead:

```csharp
namespace Shop;

using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;

public static class TypeMapSetup
{
    public static void Add(IServiceCollection services) =>
        services.AddOrionGuardEfCore<AppDbContext>(o => o
            .UseOutbox()
            .UseOutboxTypeMap(
                map => map.Map<OrderShipped>("orders.order-shipped.v1"),
                typeMap => typeMap.AllowAssemblyQualifiedNameFallback = false));
}
```

`OutboxTypeMapRegistry.Map<TEvent>` throws when a name or type is already mapped to something else. `AllowAssemblyQualifiedNameFallback` defaults to `true` so rows written before the mapping stay readable — and that is the trade-off: with the fallback on, the event type comes from the row, so anyone who can write to the outbox table (a leaked connection string, another service sharing the database, an injection elsewhere) can have any loadable `IDomainEvent` type deserialized from a payload of their choosing and dispatched to its handlers. Types that do not implement `IDomainEvent` are rejected before deserialization, which rules out general deserialization gadgets but not forged events. The worker logs a warning the first time it resolves a row this way.

To turn it off: map every event type; wait until no unprocessed row still carries an assembly-qualified name (or rewrite those rows' `EventType`); then set `AllowAssemblyQualifiedNameFallback = false`. A row naming an unmapped type is then dead-lettered with `TYPE_NOT_FOUND`.

## Waking the dispatcher

The worker waits on an `IOutboxWakeSignal`. The default `NullOutboxWakeSignal` only polls. In Outbox mode `SaveChanges`/`SaveChangesAsync` call `SignalAsync` after the save, so registering `ChannelOutboxWakeSignal` wakes a dispatcher in the same process at once (a synchronous save does not wait for a signal that completes asynchronously). Across processes, use [OrionGuard.Outbox.PostgresNotify](https://www.nuget.org/packages/OrionGuard.Outbox.PostgresNotify) or [OrionGuard.Outbox.SqlServerBroker](https://www.nuget.org/packages/OrionGuard.Outbox.SqlServerBroker). `PollingInterval` stays the upper bound either way.

## Archival

Processed rows are kept forever unless you opt in:

```csharp
namespace Shop;

using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;

public static class ArchivalSetup
{
    public static void Add(IServiceCollection services) =>
        services.AddOrionGuardEfCore<AppDbContext>(o => o
            .UseOutbox()
            .UseOutboxArchival(a => a.RetentionPeriod = TimeSpan.FromDays(7)));
}
```

| `OutboxArchivalOptions` | Default | Notes |
| --- | --- | --- |
| `RetentionPeriod` | 30 days | Rows processed longer ago than this are archived. |
| `PollingInterval` | 1 hour | The wait after a partial or empty batch; a full batch is followed immediately. |
| `BatchSize` | 1000 | Rows per batch. |
| `PreserveDeadLetters` | `true` | Rows with `Error` set are never archived. |
| `LockKey` / `LockLeaseDuration` | `orion_guard_outbox_archival` / 5 min | |

`OutboxArchivalHostedService` deletes with `DeleteOutboxArchiver` unless you register an `IOutboxArchiver`. `CopyToTableOutboxArchiver<TArchiveRow>` copies into an archive entity mapped in your context and deletes in one transaction, run through the context's execution strategy so it works under `EnableRetryOnFailure`. `BlobOutboxArchiver` writes each batch as JSON Lines to an `IOutboxArchiveSink` and then deletes — if the delete fails the batch is written again next time. Sinks: `LocalFileOutboxArchiveSink`, `RotatingFileOutboxArchiveSink`, `RetryingOutboxArchiveSink`, `CompositeOutboxArchiveSink`.

The package's one health check watches archival:

```csharp
namespace Shop;

using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

public static class ArchivalHealth
{
    public static void Add(IServiceCollection services) =>
        services.AddHealthChecks().AddCheck<OutboxArchivalHealthCheck>("outbox-archival");
}
```

It reports how long ago *this process* last completed an archival batch: Degraded after two archival polling intervals, Unhealthy after three, never below 5 and 15 minutes. Set `DegradedAfter` or `UnhealthyAfter` on a registered `OutboxArchivalHealthCheckOptions` to pin a threshold; an explicit value always wins.

## Observability

- **Tracing.** Each row stores the W3C `traceparent`/`tracestate` of `Activity.Current` at save time, and the worker starts an `Outbox.Dispatch` activity (kind `Consumer`) on the `Moongazing.OrionGuard.DomainEvents` source with that parent, so handlers run inside the original trace. Subscribe with `AddSource("Moongazing.OrionGuard.DomainEvents")`. Trace data never fails a save: a non-W3C (hierarchical) activity id stores nothing, and an over-long `tracestate` keeps the leading list members that fit.
- **Metrics.** Meters `Moongazing.OrionGuard.Outbox.Dispatcher` and `Moongazing.OrionGuard.Outbox.Archival` cover queue lag, batch size, idle polls, errors, failed polls, dead letters, lock contention, retries before success, dispatch duration, payload size, rows enqueued per save, and archival batch size, duration, failures and bytes written.
- **Time.** The dispatcher, the archival worker and `SkipLockedDistributedLock` read the clock from a `TimeProvider` in DI when one is registered, and `TimeProvider.System` otherwise — which is what makes their timing testable.

## What this does not do

- **Outbox delivery is at least once, never exactly once.** A retry re-runs *every* handler of the event, including ones that already succeeded; a crash after a successful dispatch, or a batch outliving `LockLeaseDuration` so another replica takes the lock, re-delivers too. Handlers must be idempotent.
- **Order is not guaranteed.** A failing row does not block the rows behind it, so once retries start, events are handled out of the order they were raised.
- **Inline mode cannot see a transaction EF Core does not own.** Inside an ambient `TransactionScope`, or a transaction handed in with `Database.UseTransaction()`, events are dispatched right after the save — before the commit — and one warning is logged per process. Use Outbox mode, or `Database.BeginTransaction()`, when handlers must not see uncommitted work.
- **It is not a message bus.** Events go to in-process `IDomainEventHandler<T>` implementations. Getting them onto a broker is a handler's job — see [OrionGuard.MassTransit](https://www.nuget.org/packages/OrionGuard.MassTransit).
- **Nothing dispatches without a running worker.** Outbox mode writes rows whether or not `OutboxDispatcherHostedService` is running; on a host with no background services, or a replica that never wins the lock, rows simply accumulate.
- **`TableName` is only half the rename.** It tells the worker where to look; the mapping still has to be given the same name.
- **Not NativeAOT- or trimming-safe.** `ServiceProviderDomainEventDispatcher` and `OutboxDispatcherHostedService` are marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`: handlers are resolved with `MakeGenericType`, and rows are read back with `Type.GetType` and `JsonSerializer.Deserialize(string, Type)`. The outbox uses the default `JsonSerializerOptions` with no hook for a `JsonSerializerContext`, so it depends on reflection-based serialization, which trimmed and NativeAOT apps turn off.
- **Archival health is per process.** A replica that never wins the archival lock reports Degraded — that is the check working, not a fault.

## Targets

`net8.0`, `net9.0`, `net10.0`. EF Core 10 on `net10.0` (EF Core 10 supports `net10.0` only) and EF Core 9 on `net8.0`/`net9.0`; use a provider package from the same EF Core major.

## With the rest of OrionGuard

[OrionGuard](https://www.nuget.org/packages/OrionGuard) · [OrionGuard.Locks.Redis](https://www.nuget.org/packages/OrionGuard.Locks.Redis) · [OrionGuard.Outbox.Dashboard](https://www.nuget.org/packages/OrionGuard.Outbox.Dashboard) · [OrionGuard.Outbox.PostgresNotify](https://www.nuget.org/packages/OrionGuard.Outbox.PostgresNotify) · [OrionGuard.Outbox.SqlServerBroker](https://www.nuget.org/packages/OrionGuard.Outbox.SqlServerBroker) · [OrionGuard.Testing](https://www.nuget.org/packages/OrionGuard.Testing) · [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- [Outbox locks migration guide](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/v6.4.0-outbox-locks.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
