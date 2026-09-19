# Outbox operations

`OrionGuard.EntityFrameworkCore` can dispatch domain events through a transactional outbox: the same
`SaveChanges` that stores your aggregate writes one `OutboxMessage` row per raised event, and a
hosted worker delivers them afterwards. Delivery is **at least once**. This page is the operational
half: what to set up before the first deployment, what to watch, and what to do when rows stop
moving. The API itself is on the
[OrionGuard.EntityFrameworkCore page](../packages/entityframeworkcore.md).

## Before the first deployment

1. **Map the tables.** `OutboxMessageEntityTypeConfiguration(tableName, indexFilter)` maps
   `OutboxMessage`; pass a provider-specific filter (`"[ProcessedOnUtc] IS NULL"` on SQL Server,
   `"\"ProcessedOnUtc\" IS NULL"` on PostgreSQL) so the index stays small as processed rows pile up.
   `OutboxLockEntityTypeConfiguration()` maps `OrionGuard_OutboxLocks`, which the default lock needs.
2. **Ship the migration everywhere.** If `OutboxLock` is not mapped or its table is missing, the lock
   logs one warning and the worker skips *every* poll: rows accumulate and nothing is delivered.
3. **Make handlers idempotent.** Retries, an expired lock lease and operator replays all re-run every
   handler of an event, including handlers that already succeeded.
4. **Pick a lock.** Default `SkipLockedDistributedLock` (a lease row in your database),
   `UseDistributedLock<NullDistributedLock>()` for a single instance, or
   [OrionGuard.Locks.Redis](../packages/locks-redis.md) when Redis is already there.
5. **Decide the wake signal.** Polling only (default), `ChannelOutboxWakeSignal` for a single
   process, or [PostgresNotify](../packages/outbox-postgresnotify.md) /
   [SqlServerBroker](../packages/outbox-sqlserverbroker.md) across processes. `PollingInterval`
   stays the upper bound on latency in every case.
6. **Decide retention.** Processed rows are kept forever unless archival is turned on.
7. **Wire the meters and the health check**, below.

The pieces an aggregate and its context need:

```csharp
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

public sealed record OrderShipped(Guid OrderId) : DomainEventBase;

public sealed class Order : AggregateRoot<Guid>
{
    public Order(Guid id) : base(id) { }

    public void Ship() => RaiseEvent(new OrderShipped(Id));
}

public sealed class OrderShippedHandler : IDomainEventHandler<OrderShipped>
{
    public Task HandleAsync(OrderShipped @event, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Ignore(o => o.DomainEvents);

        // The filtered index keeps the unprocessed lookup small once processed rows accumulate.
        modelBuilder.ApplyConfiguration(
            new OutboxMessageEntityTypeConfiguration("OrionGuard_Outbox", "[ProcessedOnUtc] IS NULL"));

        // Needed by the default SkipLockedDistributedLock.
        modelBuilder.ApplyConfiguration(new OutboxLockEntityTypeConfiguration());
    }
}
```

And the wiring, with the operational extras in place:

```csharp
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;
using Moongazing.OrionGuard.Outbox.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// The (sp, options) overload lets the interceptor resolve its collaborators from the context's scope.
builder.Services.AddDbContext<AppDbContext>((sp, options) => options
    .UseSqlServer(builder.Configuration.GetConnectionString("App"))
    .UseOrionGuardDomainEvents(sp));

builder.Services.AddOrionGuardDomainEvents();
builder.Services.AddOrionGuardDomainEventHandlers(typeof(Program).Assembly);

builder.Services.AddOrionGuardEfCore<AppDbContext>(o => o
    .UseOutbox(outbox =>
    {
        outbox.PollingInterval = TimeSpan.FromSeconds(5);
        outbox.BatchSize = 100;
        outbox.MaxRetries = 5;
        // Must outlast one batch: the lease is never renewed.
        outbox.LockLeaseDuration = TimeSpan.FromSeconds(30);
    })
    // Stable logical names, so renaming or moving an event type does not strand older rows.
    .UseOutboxTypeMap(map => map.Map<OrderShipped>("orders.order-shipped.v1"))
    .UseOutboxArchival(archival =>
    {
        archival.RetentionPeriod = TimeSpan.FromDays(7);
        archival.PreserveDeadLetters = true;
    }));

// Every failed attempt, transient or terminal, for alerting.
builder.Services.AddSingleton<IOutboxRowFailureObserver, LoggingRowFailureObserver>();

builder.Services.AddHealthChecks().AddCheck<OutboxArchivalHealthCheck>("outbox-archival");

builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddMeter(OutboxDispatcherDiagnostics.MeterName)
    .AddMeter(OutboxArchivalDiagnostics.MeterName));

builder.Services.AddAuthorization(options =>
    options.AddPolicy("OutboxOps", policy => policy.RequireRole("ops")));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapOutboxDashboard<AppDbContext>(o => o.AuthorizationPolicyName = "OutboxOps");

app.Run();
```

```csharp
using Microsoft.Extensions.Logging;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

public sealed class LoggingRowFailureObserver(ILogger<LoggingRowFailureObserver> logger)
    : IOutboxRowFailureObserver
{
    public Task OnRowFailedAsync(
        Guid rowId,
        string eventType,
        int attempt,
        bool isTerminal,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Observer exceptions are caught and logged by the dispatcher; the row state is already saved.
        logger.LogWarning(
            exception,
            "Outbox row {RowId} ({EventType}) failed on attempt {Attempt}. Terminal: {IsTerminal}.",
            rowId,
            eventType,
            attempt,
            isTerminal);

        return Task.CompletedTask;
    }
}
```

## Options that matter in production

| Option | Default | Operational effect |
| --- | --- | --- |
| `PollingInterval` | 5 s | Upper bound on delivery latency, and the wait after a partial, empty or failed batch. A full batch is followed immediately by the next one, so a backlog drains at full speed. |
| `BatchSize` | 100 | Rows read per poll. Larger batches drain faster but must still finish inside `LockLeaseDuration`. |
| `MaxRetries` | 5 | Failed attempts before a row is dead-lettered (`ProcessedOnUtc` stamped, `Error` kept). |
| `LockLeaseDuration` | 30 s | Never renewed. If a batch outlives it, another replica can take the lock and dispatch the same rows again. |
| `LockKey` | `orion_guard_outbox_dispatcher` | Give each application its own key if several share a database. |
| `TableName` | `OrionGuard_Outbox` | Not applied for you: pass the same name to `OutboxMessageEntityTypeConfiguration`. |
| `OutboxArchivalOptions.RetentionPeriod` | 30 days | Rows processed longer ago are archived (deleted, unless you register an `IOutboxArchiver`). |
| `OutboxArchivalOptions.PreserveDeadLetters` | `true` | Rows with `Error` set are never archived, so dead letters stay for triage. |
| `OutboxDashboardOptions.FailedRetryThreshold` | 3 | Keep it at or below `MaxRetries`, or dead letters will not be listed. |

## Several replicas

Each poll begins by taking the distributed lock; a replica that loses skips that poll and counts
`lock_contended`. Only one replica dispatches at a time, so throughput comes from `BatchSize` and the
speed of your handlers, not from adding instances. Instances still help availability, and every
listener wakes on a push signal even though only the lock holder dispatches.

Each row is dispatched in a DI scope of its own, and the row's `ProcessedOnUtc` stamp is committed in
the same transaction as any writes the handler made through the scoped `DbContext`. Every row update
is conditional on the state the row was read in, so a replay or discard made while the row is in
flight is never silently undone.

## What to watch

Meters `Moongazing.OrionGuard.Outbox.Dispatcher` and `Moongazing.OrionGuard.Outbox.Archival`
(`OutboxDispatcherDiagnostics.MeterName`, `OutboxArchivalDiagnostics.MeterName`):

| Instrument | Watch for |
| --- | --- |
| `orionguard.outbox.dispatcher.queue_lag` (ms) | The age of dispatched rows. A rising lag means the worker is behind, or is not running. |
| `orionguard.outbox.dispatcher.batch_size` | Batches pinned at `BatchSize` mean a backlog. |
| `orionguard.outbox.dispatcher.poll.idle` | Healthy when there is nothing to do; idle polls *and* growing lag mean rows are not being seen at all. |
| `orionguard.outbox.dispatcher.errors` | Per-row failures. A steady rate is a handler problem. |
| `orionguard.outbox.dispatcher.dead_lettered` | Should be zero. Every increment is a message nobody will deliver unless you replay it. |
| `orionguard.outbox.dispatcher.batch_faults` | Failures outside any row: the database is unreachable, the table is missing, a registration is absent. |
| `orionguard.outbox.dispatcher.lock_contended` | Normal with several replicas. Near-total contention on one instance suggests a stale lease row. |
| `orionguard.outbox.dispatcher.retries_before_success` | Flaky handlers or dependencies. |
| `orionguard.outbox.dispatcher.dispatch_duration_ms` | Per-row handler time. Must stay well inside `LockLeaseDuration` × `BatchSize`. |
| `orionguard.outbox.archival.*` | Batch size, duration, failures, bytes written. |

The one bundled health check watches archival: `OutboxArchivalHealthCheck` reports how long ago this
process completed an archival batch — Degraded after two archival polling intervals, Unhealthy after
three, never below 5 and 15 minutes. A replica that never wins the archival lock reports Degraded, so
in a multi-replica deployment treat it as per-instance information.

Traces: the worker starts an `Outbox.Dispatch` activity (kind `Consumer`) on the
`Moongazing.OrionGuard.DomainEvents` source, with the `traceparent` stored when the row was written,
so handler work joins the trace that produced the event. Subscribe with
`AddSource("Moongazing.OrionGuard.DomainEvents")`.

## Triage

**Nothing is being delivered and rows keep growing.**

- Look for the lock warning at startup: an unmapped `OutboxLock` or a missing
  `OrionGuard_OutboxLocks` table makes every poll skip. Apply the migration.
- Check `batch_faults` and the Error-level log it comes with: an unreachable database, a missing
  outbox table, or a missing registration.
- Confirm the host actually runs the worker (the hosted service is registered by
  `AddOrionGuardEfCore(... UseOutbox())`) and that the process is not in a crash loop.
- The EF Core InMemory provider has no conditional updates: use a relational provider.

**One row fails again and again.**

- Find it through the dashboard: `GET /_orion/outbox/failed?sort=MostRetries`. `error` holds the
  exception text, truncated to `ErrorTruncationLength`.
- Fix the cause, then `POST /_orion/outbox/{id}/replay`, which clears `RetryCount`, `Error` and
  `ProcessedOnUtc` so the next poll re-runs every handler.
- If the event is no longer wanted, `POST /_orion/outbox/{id}/discard` stamps `ProcessedOnUtc` and
  keeps `Error` and `RetryCount` for the record.

**Dead letters whose error starts `TYPE_NOT_FOUND`, `TYPE_NOT_DOMAIN_EVENT` or `DESERIALIZE_FAILED`.**

The row's event type cannot be resolved or read. These are dead-lettered on the first attempt with
no retries, so their `RetryCount` is 0 and the dashboard does not list them until you lower
`FailedRetryThreshold`. The cause is almost always a renamed, moved or deleted event type. Map stable
logical names with `UseOutboxTypeMap` before the rename, keep
`AllowAssemblyQualifiedNameFallback` on so older rows stay readable, and replay the rows once the
mapping is deployed.

**An error that starts "The event was dispatched, but saving ...".**

The handler ran, but its database writes could not be committed. Those writes were discarded and the
row counts one failed attempt, like any other failure. Look for a constraint violation in the
handler's own writes.

**Duplicate side effects.**

Expected: delivery is at least once. A crash after a successful dispatch, a batch that outlives the
lock lease, and an operator replay all re-deliver. Make handlers idempotent; there is no
de-duplication in the dispatcher.

**Delivery is slower than it should be.**

Latency is bounded by `PollingInterval` unless a wake signal is registered. In-process saves signal
directly; across processes use the PostgresNotify or SqlServerBroker package. Note that a
*synchronous* `SaveChanges()` does not wait for a signal that completes asynchronously.

**The table keeps growing.**

Turn archival on. It deletes processed rows older than `RetentionPeriod` unless you register an
`IOutboxArchiver`: `CopyToTableOutboxArchiver<TArchiveRow>` copies into an archive table in one
transaction, `BlobOutboxArchiver` writes JSON Lines to an `IOutboxArchiveSink` and then deletes.
Dead letters are preserved by default and are yours to clear.

## Operator endpoints

Routes are relative to `RoutePrefix`, default `/_orion/outbox`:

```bash
# Failed and dead-lettered rows, most retries first.
curl -s "https://app.example.com/_orion/outbox/failed?page=1&size=25&sort=MostRetries"

# Keyset paging for a large backlog: pass nextCursor back as cursor.
curl -s "https://app.example.com/_orion/outbox/failed/cursor?size=100"

# Re-queue one row, or give up on it.
curl -X POST "https://app.example.com/_orion/outbox/2f6f3b9c-6f2e-4a5a-9f3c-0b0f1f8a6a11/replay"
curl -X POST "https://app.example.com/_orion/outbox/2f6f3b9c-6f2e-4a5a-9f3c-0b0f1f8a6a11/discard"
```

- Payloads are never returned; each item carries `id`, `eventType`, `occurredOnUtc`, `retryCount`,
  `error` and `correlationId`.
- Replay answers 404 for an unknown id and 409 `already-processed-success` for a row that was
  processed without an error. Discard answers 200 with `note: "already processed"` when the row was
  already done.
- Both endpoints are a single conditional `UPDATE`, so they need a relational provider.
- The endpoints can replay and discard messages and they show event types and error text. Protect
  them: set `AuthorizationPolicyName`, or rely on the host's fallback policy. Without either, the
  group requires an authenticated user. `AllowAnonymous = true` exists for local development only.
  Set `EnableMutations = false` for a read-only mount.
- `OnMutation` runs after each successful replay or discard, with the action, the row id and the
  `HttpContext`; use it to record who did what.

## See also

- [OrionGuard.EntityFrameworkCore](../packages/entityframeworkcore.md) — inline versus outbox, the
  dispatch loop, the schema.
- [OrionGuard.Outbox.Dashboard](../packages/outbox-dashboard.md) — every endpoint and option.
- [Outbox API reference](xref:Moongazing.OrionGuard.EntityFrameworkCore.Outbox)
- [Outbox locks migration guide](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/migrations/v6.4.0-outbox-locks.md) — reference DDL per provider.
