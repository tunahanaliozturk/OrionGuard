# OrionGuard.EntityFrameworkCore

EF Core integration for [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard) domain events. A `SaveChangesAsync` interceptor collects the events raised by tracked aggregates and either dispatches them right after the save (Inline) or writes them to an outbox table in the same transaction, for a hosted worker to deliver (Outbox).

## Install

```bash
dotnet add package OrionGuard.EntityFrameworkCore
```

The core `OrionGuard` package is installed as a dependency. Add the EF Core provider package for your database yourself.

## Quick start

An aggregate, an event, and a handler:

```csharp
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

public sealed record OrderShipped(Guid OrderId) : DomainEventBase;

public sealed class Order : AggregateRoot<Guid>
{
    public Order(Guid id) : base(id) { }
    public void Ship() => RaiseEvent(new OrderShipped(Id));
}

public sealed class OrderShippedHandler : IDomainEventHandler<OrderShipped>
{
    public Task HandleAsync(OrderShipped @event, CancellationToken cancellationToken) => Task.CompletedTask;
}
```

Map the outbox and lock tables, and keep `DomainEvents` out of the model:

```csharp
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

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
```

Register everything. Use the `(sp, options)` overload of `AddDbContext` so the interceptor resolves its collaborators from the context's own scope:

```csharp
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>((sp, options) => options
    .UseSqlServer(builder.Configuration.GetConnectionString("App"))
    .UseOrionGuardDomainEvents(sp));

builder.Services.AddOrionGuardDomainEvents();
builder.Services.AddOrionGuardDomainEventHandlers(typeof(Program).Assembly);
builder.Services.AddOrionGuardEfCore<AppDbContext>(o => o.UseOutbox());
```

Then apply an EF Core migration for the two tables (see Schema and migrations below).

## Inline and Outbox

`OrionGuardEfCoreOptions.Strategy` is a `DomainEventDispatchStrategy`: `UseInline()` (the default) or `UseOutbox(...)`.

| | Inline | Outbox |
| --- | --- | --- |
| When handlers run | Inside `SaveChangesAsync`, after the save succeeds | In `OutboxDispatcherHostedService`, after the transaction commits |
| Where events live | Nowhere; they are dispatched from memory | One `OutboxMessage` row per event, written by the same `SaveChanges` as the aggregate |
| A handler throws | The exception leaves `SaveChangesAsync`, the data is already saved, and the remaining events of that save are dropped | The row is retried on later polls, then dead-lettered |
| Process dies after the save | Events are lost | Rows stay in the table and are delivered later |
| Delivery | At most once | At least once |

If you save inside your own `BeginTransaction`, Inline handlers run before your commit. In Inline mode a failed save leaves the events on the aggregates, so a retry picks them up. How handlers of one event are invoked is set by `AddOrionGuardDomainEvents(o => o.Mode = ...)`: `DispatchMode.SequentialFailFast` (default), `SequentialContinueOnError` (runs all, then throws `AggregateException`), or `Parallel`.

## Outbox delivery

Each poll, the worker takes the distributed lock, reads up to `BatchSize` rows where `ProcessedOnUtc` is null ordered by `OccurredOnUtc`, resolves and deserializes each event (System.Text.Json), dispatches it through `IDomainEventDispatcher`, stamps `ProcessedOnUtc`, and saves after every row. It then waits for the next tick, so it handles one batch per poll unless a wake signal arrives.

- **Failure.** `RetryCount` is incremented and `Error` holds the exception text. Retries happen on later polls, with no backoff beyond the polling interval. When `RetryCount` reaches `MaxRetries`, `ProcessedOnUtc` is stamped and the row becomes a dead letter (`Error` stays set).
- **Unresolvable rows** are dead-lettered on the first attempt, without retries, with an `Error` starting `TYPE_NOT_FOUND`, `TYPE_NOT_DOMAIN_EVENT`, or `DESERIALIZE_FAILED`.
- **Duplicates.** A retry re-runs every handler of the event, including the ones that already succeeded. A crash or failed row update after a successful dispatch, or a batch that outlives `LockLeaseDuration` so another replica takes the lock, also re-delivers. Handlers must be idempotent.
- **Ordering.** A failing row does not block the rows after it, so events can be handled out of order once retries happen.
- **Shared scope.** A batch runs in one DI scope. A handler that injects your `DbContext` gets the instance the worker uses to update rows, so anything it leaves tracked is saved with the row update, even after the handler throws.
- **Observer.** Register `IOutboxRowFailureObserver` (`services.AddSingleton<IOutboxRowFailureObserver, MyObserver>()`) to be told about every failed attempt, transient or terminal. Observer exceptions are logged and ignored.

## Outbox options

Set through `UseOutbox(o => ...)`. Setters throw on invalid values.

| Option | Default | Notes |
| --- | --- | --- |
| `PollingInterval` | 5 s | Must be > 0. Also the upper bound on wake latency. |
| `BatchSize` | 100 | Rows read per poll. |
| `MaxRetries` | 5 | Failed attempts before a row is dead-lettered. At least 1. |
| `LockKey` | `orion_guard_outbox_dispatcher` | Distributed lock key. |
| `LockLeaseDuration` | 30 s | Must be longer than one batch takes. The lease is not renewed. |
| `TableName` | `OrionGuard_Outbox` | Not applied for you: pass the same name to `OutboxMessageEntityTypeConfiguration`. |

## Schema and migrations

- `new OutboxMessageEntityTypeConfiguration(tableName, indexFilter = null)` maps `OutboxMessage` and creates `IX_OrionGuard_Outbox_Unprocessed`. Without a filter it is a composite index on `(ProcessedOnUtc, OccurredOnUtc)`. With a provider-specific filter, for example `"[ProcessedOnUtc] IS NULL"` on SQL Server or `"\"ProcessedOnUtc\" IS NULL"` on PostgreSQL, it is a filtered index on `OccurredOnUtc`, which stays small as processed rows pile up.
- `new OutboxLockEntityTypeConfiguration()` maps the `OrionGuard_OutboxLocks` table. It is needed only with the default `SkipLockedDistributedLock`.

Generate the migration with `dotnet ef migrations add` as usual. Reference DDL per provider is in the [outbox locks migration guide](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/v6.4.0-outbox-locks.md).

## Distributed lock

Outbox mode registers `SkipLockedDistributedLock`: one lease row per key in `OrionGuard_OutboxLocks`, taken with a conditional `UPDATE`/`INSERT` in a transaction and confirmed by reading the holder back. A replica that loses skips that poll. If the table is missing, the lock logs one warning and the worker skips every poll, so rows accumulate until the migration is applied.

- `UseDistributedLock<NullDistributedLock>()` always acquires. Use it for a single instance; no lock table is needed.
- `UseDistributedLock<TLock>()` plugs in any `IDistributedLock`. [OrionGuard.Locks.Redis](https://www.nuget.org/packages/OrionGuard.Locks.Redis) provides a Redis one.
- The archival worker uses the same `IDistributedLock` with its own key.

## Type mapping

By default a row stores the event's assembly-qualified name and is resolved with `Type.GetType`, so renaming or moving an event type strands older rows. Map stable logical names instead:

```csharp
using Moongazing.OrionGuard.EntityFrameworkCore;

builder.Services.AddOrionGuardEfCore<AppDbContext>(o => o
    .UseOutbox()
    .UseOutboxTypeMap(
        map => map.Map<OrderShipped>("orders.order-shipped.v1"),
        typeMap => typeMap.AllowAssemblyQualifiedNameFallback = false));
```

`OutboxTypeMapRegistry.Map<TEvent>` throws on a name or type that is already mapped to something else. `AllowAssemblyQualifiedNameFallback` defaults to `true`, which keeps rows written before the mapping readable.

## Push wake-up

The worker waits on an `IOutboxWakeSignal`. The default `NullOutboxWakeSignal` only polls. In Outbox mode `SaveChangesAsync` calls `SignalAsync` after the commit, so registering `ChannelOutboxWakeSignal` wakes a dispatcher in the same process immediately. For cross-process wake-ups use [OrionGuard.Outbox.PostgresNotify](https://www.nuget.org/packages/OrionGuard.Outbox.PostgresNotify) or [OrionGuard.Outbox.SqlServerBroker](https://www.nuget.org/packages/OrionGuard.Outbox.SqlServerBroker). `PollingInterval` stays the upper bound either way.

## Archival

Processed rows are kept forever unless you opt in:

```csharp
using Moongazing.OrionGuard.EntityFrameworkCore;

builder.Services.AddOrionGuardEfCore<AppDbContext>(o => o
    .UseOutbox()
    .UseOutboxArchival(a => a.RetentionPeriod = TimeSpan.FromDays(7)));
```

| `OutboxArchivalOptions` | Default | Notes |
| --- | --- | --- |
| `RetentionPeriod` | 30 days | Rows processed longer ago than this are archived. |
| `PollingInterval` | 1 hour | One batch per interval. |
| `BatchSize` | 1000 | Rows per batch, so at most 24,000 rows a day at the defaults. |
| `PreserveDeadLetters` | `true` | Rows with `Error` set are never archived. |
| `LockKey` / `LockLeaseDuration` | `orion_guard_outbox_archival` / 5 min | |

`OutboxArchivalHostedService` deletes with `DeleteOutboxArchiver` unless you register an `IOutboxArchiver`: `CopyToTableOutboxArchiver<TArchiveRow>` copies rows into an archive entity mapped in your context and deletes them in one transaction, and `BlobOutboxArchiver` writes each batch as JSON Lines to an `IOutboxArchiveSink` and then deletes (if the delete fails, the batch is written again next time). Sinks: `LocalFileOutboxArchiveSink`, `RotatingFileOutboxArchiveSink`, `RetryingOutboxArchiveSink`, `CompositeOutboxArchiveSink`. Dead letters stay in the table for you to inspect, replay, or discard, for example with [OrionGuard.Outbox.Dashboard](https://www.nuget.org/packages/OrionGuard.Outbox.Dashboard).

The only health check in the package watches archival:

```csharp
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

builder.Services.AddSingleton(new OutboxArchivalHealthCheckOptions
{
    DegradedAfter = TimeSpan.FromHours(2),
    UnhealthyAfter = TimeSpan.FromHours(3),
});
builder.Services.AddHealthChecks().AddCheck<OutboxArchivalHealthCheck>("outbox-archival");
```

It reports how long ago this process last completed an archival batch. The defaults (5 and 15 minutes) are shorter than the default 1-hour `PollingInterval`, so raise them as above. A replica that never wins the archival lock reports Degraded.

## Observability

- **Tracing.** Each outbox row stores the W3C `traceparent`/`tracestate` of `Activity.Current` at save time. The worker starts an `Outbox.Dispatch` activity (kind `Consumer`) on the `Moongazing.OrionGuard.DomainEvents` ActivitySource with that parent, so handlers run inside the original trace when that source is listened to. Subscribe with `AddSource("Moongazing.OrionGuard.DomainEvents")`.
- **Metrics.** Meters `Moongazing.OrionGuard.Outbox.Dispatcher` and `Moongazing.OrionGuard.Outbox.Archival` (`OutboxDispatcherDiagnostics.MeterName`, `OutboxArchivalDiagnostics.MeterName`) cover queue lag, batch size, idle polls, errors, dead letters, lock contention, retries before success, dispatch duration, payload size, and archival batch size, duration, failures, and bytes written.

## Known limitations

- Only `SaveChangesAsync` is intercepted. A synchronous `SaveChanges()` neither dispatches (Inline) nor writes outbox rows (Outbox); the events stay on the aggregate.
- `SkipLockedDistributedLock` sends raw SQL with unquoted names (`UPDATE OrionGuard_OutboxLocks SET HolderId = ...`). PostgreSQL folds those to lower case, so they miss the quoted `"OrionGuard_OutboxLocks"` table that EF Core creates; the lock treats that as a missing table and the worker never dispatches. Its tests run on SQLite only. On PostgreSQL, use OrionGuard.Locks.Redis, or `NullDistributedLock` for a single instance.
- `CopyToTableOutboxArchiver` calls `SaveChangesAsync` inside its own transaction. With a retrying execution strategy (`EnableRetryOnFailure`), EF Core rejects that with `InvalidOperationException`, so every batch that has rows to move fails and is logged.
- NativeAOT: `ServiceProviderDomainEventDispatcher` (the default dispatcher) and `OutboxDispatcherHostedService` are marked `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. Handlers are resolved with `MakeGenericType`, and rows are read back with `Type.GetType` and `JsonSerializer.Deserialize(string, Type)`. The outbox uses the default `JsonSerializerOptions` and has no hook for a `JsonSerializerContext`, so it depends on reflection-based serialization, which NativeAOT and trimmed apps turn off by default.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- EF Core (`Microsoft.EntityFrameworkCore` and `.Relational`) 9.0.20 on net8.0 and net9.0, 10.0.12 on net10.0. Use a provider package from the same major version.
- `Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` 10.0.12 on every target

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- [Outbox locks migration guide](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/v6.4.0-outbox-locks.md)
- Related packages: [OrionGuard](https://www.nuget.org/packages/OrionGuard), [OrionGuard.Locks.Redis](https://www.nuget.org/packages/OrionGuard.Locks.Redis), [OrionGuard.Outbox.Dashboard](https://www.nuget.org/packages/OrionGuard.Outbox.Dashboard), [OrionGuard.Outbox.PostgresNotify](https://www.nuget.org/packages/OrionGuard.Outbox.PostgresNotify), [OrionGuard.Outbox.SqlServerBroker](https://www.nuget.org/packages/OrionGuard.Outbox.SqlServerBroker), [OrionGuard.OpenTelemetry](https://www.nuget.org/packages/OrionGuard.OpenTelemetry)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
