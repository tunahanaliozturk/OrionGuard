# Changelog

All notable changes to OrionGuard will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [6.5.29] - 2026-06-15

### Added

#### `orionguard.outbox.dispatcher.lock_contended` counter

`Counter<long>` increments each dispatcher cycle in which this replica fails to acquire the multi-instance distributed lock because another replica holds the lease.

- Distinct from the v6.5.17 `poll.idle` counter, which fires only AFTER the lock is held and the backlog is found empty. `lock_contended` fires when the replica never became the active dispatcher at all.
- Operators graph it per replica to confirm exactly one replica is dispatching (the others should sit mostly contended), to spot a stuck or dead leader (a sudden drop in one replica's contention rate without another picking up), and to right-size the dispatcher replica count.
- Public `OutboxDispatcherDiagnostics.RecordLockContended()` helper.

### Tests

- `LockContendedCounterTests`: `RecordLockContended` increments the counter.

## [6.5.28] - 2026-06-15

### Added

#### `orionguard.outbox.dispatcher.dead_lettered` counter

`Counter<long>` increments once for every row the dispatcher permanently abandons: an unresolvable or non-`IDomainEvent` type, a payload that fails to deserialize, or a transient failure that finally exhausted `MaxRetries`.

- Distinct from the v6.5.18 `errors` counter, which fires on EVERY swallowed failure (transient retries included). Operators alert on the dead-letter rate as the SLO signal that rows are being lost, which the much higher errors rate dilutes.
- Emitted only AFTER the row's terminal state is persisted (the post-`SaveChangesAsync` block, alongside the deferred success metrics): `NotifyRowFailureAsync` now returns the terminal exception type and the loop records it post-persist, so a `SaveChanges` failure that re-dispatches the row does not double-count (codex/CodeRabbit P2).
- Tag: `exception_type` (the terminal cause), for triage.
- Public `OutboxDispatcherDiagnostics.RecordDeadLetter(string)` helper.

### Tests

- `DeadLetteredCounterTests`: emits a measurement tagged with the exception type.
- `OutboxDispatcherTests.ProcessBatch_DeadLetter_EmitsTheDeadLetteredCounterAfterPersistence`: a real dead-letter driven through the loop emits the counter via the post-persist path.

## [6.5.27] - 2026-06-15

### Added

#### `orionguard.outbox.dispatcher.retries_before_success` histogram

`Histogram<int>` records the retry count a row had accumulated at the moment it dispatched successfully (its `RetryCount` on the success path: 0 = succeeded on the first attempt). It measures the successful side of the dispatch loop, complementing the v6.5.18 `errors` counter (failures) and the dead-letter path (terminal only), so operators can answer "are retries quietly papering over downstream flakiness?".

- A healthy system sits at p50 = 0; a rising upper percentile means rows are increasingly succeeding only after transient downstream failures.
- Unlike the batch-size histograms, the zero sample IS recorded: the fraction of first-try successes is exactly the signal, so dropping zeros would erase the healthy baseline.
- Emitted post-persist (after `SaveChangesAsync`), alongside `queue_lag` (v6.5.16) and `row_size_bytes` (v6.5.19), so a SaveChanges failure that re-dispatches the row does not double-count.
- Public `OutboxDispatcherDiagnostics.RecordRetriesBeforeSuccess(int)` helper (negatives clamped to 0).

### Tests

- `RetriesBeforeSuccessHistogramTests`: first-try zero is recorded, the retry count is emitted, negatives clamp to 0.

## [6.5.26] - 2026-06-13

### Added

#### `orionguard.outbox.archival.failures` counter

`Counter<long>` increments when an archival batch throws and is swallowed by the worker's catch block. Operators alert on the rate to catch a stuck archival pipeline that the v6.5.14 liveness gauge alone cannot distinguish from a healthy-but-idle worker.

- Tag: `exception_type`.
- Public `OutboxArchivalDiagnostics.RecordArchiveFailure(string)` helper.
- Completes the archival health picture: batch_size (v6.5.20) + duration_ms (v6.5.21) + liveness (v6.5.14) + failures (v6.5.26).

### Tests

1 fact.

### Migration from v6.5.25

Source-compatible.

## [6.5.25] - 2026-06-12

### Added

#### `orionguard.outbox.dispatcher.batch_size` histogram

`Histogram<int>` of rows claimed per dispatcher poll cycle. Operators graph p99 to spot a dispatcher consistently maxing out `BatchSize` (raise the batch / parallelism) or staying near zero (over-sized polling cadence).

- Zero-row cycles do NOT emit (idle polling is the v6.5.17 idle-poll counter's job).
- Mirrors v0.7.18 Audit and v0.2.16 Patch batch_size shapes on the Guard side - all three outbox families now expose the same poll-outcome triple (batch_size + idle + errors).
- Public `OutboxDispatcherDiagnostics.RecordDispatcherBatchSize(int)` helper.

### Tests

2 facts.

### Migration from v6.5.24

Source-compatible.

## [6.5.24] - 2026-06-12

### Added

#### `orionguard.outbox.dispatcher.dispatch_duration_ms` histogram

`Histogram<double>` measuring per-row `IDomainEventDispatcher.DispatchAsync` wall-clock. Operators graph p99 to isolate consumer-side dispatch cost from queue_lag (which sums queue time + dispatch + commit).

- ALL outcomes emit (try/finally).
- Negative values clamped to 0.
- Public `RecordDispatchDuration(double)` helper.

### Tests

2 facts.

### Migration from v6.5.23

Source-compatible.

## [6.5.23] - 2026-06-12

### Added

#### `IOutboxRowFailureObserver` extensibility

Consumer-supplied observer invoked when the dispatcher swallows a per-row failure. Mirror of v0.2.18 Patch `IDeadLetterSink` on the Guard side.

- `IOutboxRowFailureObserver` interface + `NullOutboxRowFailureObserver` default.
- Optional 8th ctor parameter on `OutboxDispatcherHostedService`.
- Fires for EVERY swallowed failure with `attempt` + `isTerminal` flags.
- Observer fires BEFORE `SaveChangesAsync` so the notification is best-effort.

### Tests

2 facts.

### Migration from v6.5.22

Source-compatible.

## [6.5.22] - 2026-06-11

OrionGuard `orionguard.outbox.enqueued_rows_per_save` histogram (post-commit AsyncLocal pattern).

## [6.5.21] - 2026-06-11

### Added

#### `orionguard.outbox.archival.duration_ms` histogram

`Histogram<double>` measuring `OutboxArchivalHostedService.ArchiveBatchAsync` wall-clock per cycle. Operators graph p99 to spot a backend whose archive write throughput has regressed independently of row count (slow blob sink keeps the dispatcher honest but hurts throughput).

- ALL cycles emit including zero-row (poll cost matters too).
- Recorded around the full `archiver.ArchiveAsync` round-trip.
- Public `OutboxArchivalDiagnostics.RecordArchiveCycleDuration(double)` helper; negative inputs are clamped to 0 to tolerate clock skew across hosts.

### Tests

2 facts.

### Migration from v6.5.20

Source-compatible.

## [6.5.17] - [6.5.20]

Released to NuGet; see GitHub release notes for `orionguard.outbox.dispatcher.poll.idle` counter (v6.5.17), `dispatcher.errors` counter (v6.5.18), `dispatcher.row_size_bytes` histogram (v6.5.19), `archival.batch_size` histogram (v6.5.20).

## [6.5.16] - 2026-06-11

### Added

#### `orionguard.outbox.dispatcher.queue_lag` histogram

`Histogram<double>` exposed via the new `Moongazing.OrionGuard.Outbox.Dispatcher` Meter. Records per-row dispatch lag (`OccurredOnUtc -> ProcessedOnUtc`) on the success path so operators graph p50/p99 and spot dispatcher slowdown BEFORE rows pile up beyond the steady-state dispatched-count rate.

- Recorded in `OutboxDispatcherHostedService` immediately after `dispatcher.DispatchAsync` succeeds.
- Clock-skew negative deltas are clamped to 0 so they do not pull p50 down.
- Public `OutboxDispatcherDiagnostics.RecordQueueLag(double)` so consumer-owned dispatchers can opt in.
- Dead-letter paths do NOT emit; that latency is a separate operational signal.

### Tests

2 new facts.

### Migration from v6.5.15

Source-compatible.

## [6.5.15] - 2026-06-11

### Added

#### `OutboxDashboardOptions.SecurityHeaders`

Every dashboard response now carries production-friendly hardening headers via an endpoint filter on the route group.

Defaults:
- `X-Frame-Options: DENY` (clickjacking guard)
- `X-Content-Type-Options: nosniff` (MIME-sniff guard)
- `Referrer-Policy: no-referrer`
- `Cache-Control: no-store`

The filter writes headers BEFORE the response body so they apply even when the endpoint short-circuits via `Results.NotFound()`.

Consumers can set `SecurityHeaders` to an empty dictionary to disable defaults, or assign a custom dictionary to replace them entirely (no merge).

### Tests

3 new facts.

### Migration from v6.5.14

Source-compatible. Existing deployments inherit the defaults transparently.

## [6.5.14] - 2026-06-11

### Added

#### `OutboxArchivalHealthCheck` and `OutboxArchivalState`

`IHealthCheck` that watches the `OutboxArchivalHostedService` liveness via a shared `OutboxArchivalState` singleton. Operators wire it into ASP.NET Core / generic-host pipelines so a stuck archival worker downgrades the `/health` probe before rows pile up.

- `OutboxArchivalState.RecordSuccessfulBatch(DateTime)` called by the hosted service after every batch (including 0-row batches - the goal is liveness, not throughput).
- `OutboxArchivalHealthCheck` returns `Healthy` / `Degraded` / `Unhealthy` based on the elapsed time since the last successful batch.
- `OutboxArchivalHealthCheckOptions` (`DegradedAfter` default 5 min, `UnhealthyAfter` default 15 min) validated at construction.
- Pre-startup state (no batch yet) reported as `Degraded` so operators can tell "warming up" apart from "stuck".
- New 6-arg hosted-service ctor wires the optional state mirror; existing 5- and 4-arg ctors still work (zero-binary-break).
- `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` added as a package reference.

### Tests

6 new facts.

### Migration from v6.5.13

Source-compatible.

```csharp
services.AddSingleton<OutboxArchivalState>();
services.AddHealthChecks()
    .AddCheck<OutboxArchivalHealthCheck>("orionguard-outbox-archival");
```

## [6.5.13] - 2026-06-11

### Added

#### `orionguard.outbox.archive.bytes_written` counter

OTel `Counter<long>` exposed via the new `Moongazing.OrionGuard.Outbox.Archival` Meter. Lets operators graph archival throughput / storage growth without scraping S3 / blob listings.

- Tag: `sink` - short identifier each sink supplies (`local-file`, `rotating-file`).
- `OutboxArchivalDiagnostics.RecordBytes(long bytes, string sinkName)` public so consumer-owned sinks (Azure Blob, S3, GCS) can opt in.
- `LocalFileOutboxArchiveSink` and `RotatingFileOutboxArchiveSink` now record on every successful flush.
- Non-positive payloads ignored.

### Tests

3 new facts.

### Migration from v6.5.12

Source-compatible. Custom sinks opt-in by calling `RecordBytes` after their inner write succeeds.

## [6.5.12] - 2026-06-11

### Added

#### `CompositeOutboxArchiveSink` - fan-out to multiple sinks

Deployments wanting archival redundancy can now wire two or more `IOutboxArchiveSink` instances (e.g. S3 + local rotating file) and the composite forwards every write to each in registration order.

- `CompositeOutboxArchiveSink(IEnumerable<IOutboxArchiveSink>, CompositeOutboxArchiveSinkMode)`.
- `FailFast` (default): first failure aborts and bubbles. Use when cross-sink consistency matters.
- `BestEffort`: every sink is called; AggregateException is thrown ONLY if every sink threw. Use when archive needs to land in at least one destination.
- Cancellation propagates without consuming remaining sinks.
- Sequential fan-out by design - cloud-object-store clients benefit from serial calls more than from fan-out concurrency.

### Tests

8 new facts.

### Migration from v6.5.11

Source-compatible.

## [6.5.11] - 2026-06-11

### Added

#### `RetryingOutboxArchiveSink` decorator

Decorator that wraps any `IOutboxArchiveSink` with jittered exponential retry. Useful for cloud-object-store sinks (S3, Azure Blob, GCS) where transient 503 / socket errors are common - instead of failing the archival sweep on the first transient failure, the decorator retries up to `MaxAttempts` times before giving up.

- `RetryingOutboxArchiveSink(inner, options)`.
- `RetryingOutboxArchiveSinkOptions`: `MaxAttempts` (default 5), `BaseDelay` (100 ms), `MaxDelay` (5 s), `IsRetryable` predicate (default retries everything), `RandomFactory` for seeded testing.
- Backoff: `BaseDelay * 2^(attempt-1)` capped at `MaxDelay`, jittered in `[0.5x, 1.0x]` of the computed value.
- Cancellation propagates immediately without consuming a retry slot.
- Options validated at construction.

### Tests

6 new facts.

### Migration from v6.5.10

Source-compatible.

## [6.5.10] - 2026-06-11

### Added

#### `RotatingFileOutboxArchiveSink` - production-grade local archive sink

Extends the v6.5.9 `IOutboxArchiveSink` abstraction. v6.5.9 shipped `LocalFileOutboxArchiveSink` as a single-file reference; v6.5.10 ships the production-grade rotating variant for deployments that ship archives to a local NFS / EFS mount + a downstream batch job that uploads to cold storage.

- `RotatingFileOutboxArchiveSink` writes JSON Lines payloads under `{Root}/{yyyy-MM-dd}/{keyHint}-{shard:D4}.jsonl`.
- One subdirectory per UTC day so the operator can prune by date.
- Within a day, files split at `MaxFileBytes` (default 64 MiB). The sink appends to the lowest-numbered shard whose size + payload fits under the cap; otherwise it rolls to the next shard.
- `MaxShardsPerDay` (default 9999) bounds the per-day shard count - reaching the cap throws so a runaway archive load surfaces as an explicit configuration error rather than silently overwriting.

### Tests

6 new facts.

### Migration from v6.5.9

Source-compatible.

## [6.5.9] - 2026-06-10

### Added

#### `IOutboxArchiveSink` + `BlobOutboxArchiver` for off-box archival

Extends the v6.5.6 `IOutboxArchiver` strategy hook. v6.5.6 introduced the `CopyToTableOutboxArchiver` archive-table pattern; v6.5.9 ships the off-box version: rows leave the database entirely after archival, landing in a consumer-supplied blob sink (S3, Azure Blob, GCS, local filesystem).

- `IOutboxArchiveSink` abstraction with a single `WriteAsync(string keyHint, ReadOnlyMemory<byte> payload, ct)` call.
- `BlobOutboxArchiver` orchestrates SELECT eligible rows -> serialise to newline-delimited JSON (`.jsonl`) -> `sink.WriteAsync` -> `ExecuteDelete` with re-checked eligibility (matches v6.5.6 safety).
- `LocalFileOutboxArchiveSink` reference implementation writes `.jsonl` files to a local directory.
- Sink failure aborts the sweep WITHOUT deleting; rows stay on the live table for the next tick.

### Tests

6 new facts; 30 dashboard + 15 archival facts total.

### Migration from v6.5.8

Source-compatible.

## [6.5.8] - 2026-06-10

### Added

#### Outbox dashboard cursor-based pagination

The v6.5.4-6.5.7 `/failed` endpoint used offset pagination (`?page=N&size=M`). Offset pagination grows expensive on large failed-message tables (each page does an OFFSET scan of every preceding row) and is unstable when new rows arrive mid-paging (the same row can appear on consecutive pages, or be skipped entirely). v6.5.8 adds a cursor variant that uses keyset pagination: the next page's WHERE predicate seeks past the last-seen `(sortKey, Id)` pair so the database does an index seek instead of an OFFSET scan.

- **`GET /_orion/outbox/failed/cursor?cursor=<token>&size=N&sort=<axis>`**: paginated endpoint that returns up to `size` rows past the cursor's last position, plus the `nextCursor` for the page after. The first call sends no cursor; subsequent calls send the `nextCursor` from the previous response.
- **`OutboxFailedCursor`** opaque token: base64Url-encoded `(LastOccurredOnUtcTicks, LastId, LastRetryCount, Sort)`. Internal-only - clients treat it as opaque. The sort axis is encoded INTO the cursor so a caller cannot switch sort mid-paging and get duplicate / skipped rows.
- **One-extra-row peek**: the endpoint fetches `size + 1` rows; if the peek row exists, slice it off and emit a `nextCursor` based on the LAST returned row (not the peek). Detects "is there another page" without a separate COUNT round-trip.
- **Invalid / garbage cursors** fall back to the no-cursor path (start of results) rather than 400. Matches the dashboard's existing forgiving query-string behaviour (invalid `sort` also falls back rather than failing the request).
- **Stable tiebreaker**: every sort axis appends `Id` as a secondary key so rows with identical `OccurredOnUtc` / `RetryCount` stay in deterministic order across pages.

### Tests

5 new facts: cursor pages through 25 rows in chunks of 10 with no duplicates / no skips, `hasNextPage=true` + `nextCursor` set when more rows remain, `hasNextPage=false` + `nextCursor=null` on last page, invalid cursor falls back to beginning, cursor locks sort axis (switched `?sort=` on a follow-up call is ignored). 30 dashboard facts total.

### Migration from v6.5.7

Source-compatible. The existing `/failed` offset endpoint continues to work; the cursor endpoint is an additive new route. Operator UIs that page through large tables should switch to the cursor variant:

```
GET /_orion/outbox/failed/cursor?size=50&sort=MostRetries
-> { items: [...], nextCursor: "AAAB...", hasNextPage: true, ... }
GET /_orion/outbox/failed/cursor?cursor=AAAB...&size=50
-> next page
```

## [6.5.7] - 2026-06-10

### Added

#### Dashboard sort + richer pagination metadata

The v6.5.4 `/failed` listing only supported `OccurredOnUtc` ascending and returned `{page, size, total, items}`. v6.5.7 adds an explicit sort axis and the navigation metadata operators expect for paged tables.

- **`OutboxFailedListingSort`** enum: `OldestFirst` (default), `NewestFirst`, `MostRetries`. Query string: `?sort=newestfirst` (case-insensitive enum name match).
- **`OutboxDashboardOptions.DefaultSort`** controls the default when the consumer omits the query string. Default `OldestFirst` so operators triage the longest-failing rows first.
- **Response shape extended** with `totalPages`, `hasNextPage`, `hasPreviousPage`, and `sort` (the resolved enum name). Existing `page` / `size` / `total` / `items` fields unchanged; consumers depending on the v6.5.4-6.5.6 shape continue to parse cleanly.
- **`MostRetries`** order falls back to `OccurredOnUtc` ascending as a stable tiebreaker so rows with the same retry count stay in deterministic order.

### Tests

4 new facts: pagination metadata returned (total/totalPages/hasNextPage/hasPreviousPage), `NewestFirst` orders descending by `OccurredOnUtc`, `MostRetries` orders descending by `RetryCount`, invalid sort falls back to default. 24 dashboard facts total.

`ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))` added to the in-memory test fixture - additional fixtures pushed the EF Core internal-service-provider count past the framework's 20-instance warning threshold.

### Migration from v6.5.6

Source-compatible. Existing query strings without `?sort=...` use the configured `DefaultSort`.

```csharp
app.MapOutboxDashboard<AppDbContext>(o =>
{
    o.DefaultSort = OutboxFailedListingSort.MostRetries; // triage most-retried first by default
});
```

## [6.5.6] - 2026-06-10

### Added

#### `IOutboxArchiver` strategy hook

Outbox archival worker (pre-existing) gains a pluggable strategy hook so consumers can swap the default delete-on-retention behaviour for compliance-friendly archive-table copies without forking the hosted service.

- **`IOutboxArchiver`** interface: `Task<int> ArchiveAsync(DbContext, DateTime cutoff, OutboxArchivalOptions, CancellationToken)`. Implementations honour `OutboxArchivalOptions.PreserveDeadLetters` and `BatchSize`; return value is the row count for the hosted service's logging / metrics.
- **`DeleteOutboxArchiver`**: the default. Same `ExecuteDeleteAsync` semantics as the pre-v6.5.6 inline code; ordering by `ProcessedOnUtc` so the oldest rows leave first. Honours `PreserveDeadLetters`.
- **`CopyToTableOutboxArchiver<TArchiveRow>`** (generic): COPIES eligible rows into a consumer-owned archive table, then deletes the originals in the SAME transaction so no row is lost across the boundary. Consumer supplies a `Func<OutboxMessage, TArchiveRow>` projection. Useful for retention regimes that must keep the dispatched-event trail past the live-table window.
- **`OutboxArchivalHostedService`** ctor gains an optional `IOutboxArchiver?` parameter (position 5, after the existing `ILogger?`). Null defaults to `DeleteOutboxArchiver` so existing wiring keeps working without changes. `ArchiveBatchAsync` now delegates to the archiver instead of executing the delete inline.

### Tests

2 new facts cover the strategy hook: `CopyToTableOutboxArchiver` copies rows to a separate table AND deletes originals; a custom no-op archiver is honoured (default delete path is bypassed). 9 archival facts total.

### Migration from v6.5.5

Source-compatible. Existing `new OutboxArchivalHostedService(opts, scopeFactory, distributedLock, logger)` calls keep the delete-on-retention behaviour. Opt into copy-to-table archival:

```csharp
services.AddSingleton<IOutboxArchiver>(_ => new CopyToTableOutboxArchiver<OutboxArchiveRow>(m => new OutboxArchiveRow
{
    Id = m.Id,
    EventType = m.EventType,
    Payload = m.Payload,
    OccurredOnUtc = m.OccurredOnUtc,
    ArchivedOnUtc = DateTime.UtcNow,
}));
```

## [6.5.5] - 2026-06-10

### Added

#### Outbox dashboard mutation surface

Completes the operator workflow started in v6.5.4. The read surface listed failed / dead-lettered messages; v6.5.5 lets operators actually act on them.

- **`POST /_orion/outbox/{id:guid}/replay`** finds the row, clears `RetryCount`, clears `Error`, clears `ProcessedOnUtc` (in case the dispatcher had already dead-lettered it), commits via `SaveChangesAsync`, and returns `200 OK` with `{ id, action: "replay" }`. Unknown id -> `404 Not Found`.
- **`POST /_orion/outbox/{id:guid}/discard`** stamps `ProcessedOnUtc = UtcNow` so the dispatcher loop skips the row on its next pass; `Error` and `RetryCount` stay intact so future operators see the history. Idempotent: a row that already has `ProcessedOnUtc` set returns `200 OK` with `{ id, action: "discard", note: "already processed" }` and the audit hook does NOT fire again. Unknown id -> `404 Not Found`.
- **`OutboxDashboardOptions.OnMutation`** optional `Func<OutboxMutationEvent, Task>` audit hook fires after a successful replay / discard. The dashboard itself never writes audit rows so consumers stay in control of storage shape and retention. `OutboxMutationEvent` carries `Action`, `OutboxMessageId`, `HttpContext`, and `OccurredAtUtc` so consumers stamp `User.Identity.Name`, claims, IP, etc. Throwing from the hook does NOT roll back the database commit.
- **`OutboxDashboardOptions.EnableMutations`** (default `true`) - set `false` for strictly read-only mounts. When false the `POST` endpoints are not registered at all (return `404`) while the existing `/failed` listing stays available.

### Tests

5 new endpoint tests (replay path, discard path, replay 404, discard 404, discard idempotency) + 3 mutation-hook tests (fires on replay, does NOT fire on 404, does NOT fire on already-processed discard) + 2 read-only-mode tests (POST returns 404 when disabled, GET still works). 18 facts total in the dashboard test suite.

### Migration from v6.5.4

Source-compatible. Existing dashboard registrations get `replay` and `discard` automatically; opt out with `o.EnableMutations = false`.

```csharp
app.MapOutboxDashboard<AppDbContext>(o =>
{
    o.OnMutation = async evt =>
    {
        await auditWriter.WriteAsync(new
        {
            evt.Action,
            evt.OutboxMessageId,
            User = evt.HttpContext.User.Identity?.Name,
            Ip = evt.HttpContext.Connection.RemoteIpAddress?.ToString(),
            evt.OccurredAtUtc,
        });
    };
});
```

## [6.5.4] - 2026-06-09

### Added

#### `Moongazing.OrionGuard.Outbox.Dashboard` (NEW PACKAGE)

Read-only operator dashboard for the OrionGuard outbox. Maps an authorized HTTP endpoint group that lists failed / poisoned messages from the consumer's DbContext.

- **`MapOutboxDashboard<TDbContext>(this IEndpointRouteBuilder, configure?)`**: registers a route group under the configured `RoutePrefix` (default `/_orion/outbox`). The group calls `RequireAuthorization()` by default so the host's fallback policy applies; consumers pass a named policy or opt anonymous (NOT recommended).
- **`GET /_orion/outbox/failed?page=N&size=M`**: paginated listing of rows where `RetryCount >= FailedRetryThreshold` (default 3, matching v6.5.0 dispatcher default) AND `ProcessedOnUtc IS NULL`. Pagination clamps to `MaxPageSize` (default 100); response includes `{page, size, total, items[]}`.
- **`OutboxFailedMessageRow`**: read-only projection excluding `Payload` to limit blast-radius if authorization is misconfigured. Error text is truncated to `ErrorTruncationLength` (default 1024 chars); full text remains in the database.
- **`OutboxDashboardOptions` validation**: empty `RoutePrefix`, non-positive page sizes / retry threshold, and negative truncation length all throw `InvalidOperationException` at endpoint registration time so misconfigured deployments fail fast.

### Deferred

- **Replay / discard actions** -> v6.5.5 (originally targeted v6.5.4; the read-only surface ships now so operators see poisoned messages while the mutation surface gets a focused review)

### Migration from v6.5.3

Source-compatible. The dashboard is an opt-in add-on package: install `OrionGuard.Outbox.Dashboard`, register your DbContext via `AddDbContext<TDbContext>(...)`, then call `app.MapOutboxDashboard<TDbContext>()` from your endpoint configuration.

```csharp
app.MapOutboxDashboard<AppDbContext>(o =>
{
    o.AuthorizationPolicyName = "OutboxOps";
});
```

## [6.5.3] - 2026-06-09

### Added

#### `Moongazing.OrionGuard.Outbox.SqlServerBroker` (NEW PACKAGE)

SQL Server Service Broker backed `IOutboxWakeSignal` for `OrionGuard.EntityFrameworkCore`. Sibling to v6.5.2's PostgresNotify add-on for the SQL Server provider.

- **`SqlServerBrokerOutboxWakeSignal`**: `BackgroundService` that holds a dedicated `SqlConnection` and runs `WAITFOR (RECEIVE ... FROM <queue>) TIMEOUT <ms>`. On RECEIVE, the in-process channel wakes the dispatcher. Reconnect loop with exponential back-off bounded by `MaxReconnectDelay`.
- **`SqlServerBrokerOptions`**: `ConnectionString` (required), `QueueName` (default `OrionGuardOutboxQueue`), `ServiceName` (default `OrionGuardOutboxService`), `ReceiveTimeout` (default 30s), reconnect-delay tuning.
- **`SqlServerBrokerSetupSql.Create / Drop`**: idempotent T-SQL helpers that install the Service Broker message type, contract, queue, service, and AFTER INSERT trigger. The package deliberately does NOT auto-install schema changes; consumers run the SQL once via an EF Core migration AFTER enabling Service Broker on the database (`ALTER DATABASE [...] SET ENABLE_BROKER`).
  - Bracketed-identifier escape (`]` -> `]]`) for table / queue / service names.
  - Quoted-literal escape (`'` -> `''`) for service / contract / message-type names.
  - Double-quoted escape (`'` -> `''''`) for the EXEC-nested trigger body's SEND TO SERVICE literal, so a service name containing a single quote does not malform the EXEC string.
- **DI**: `services.AddSqlServerBrokerOutboxWakeSignal(o => o.ConnectionString = "...")` registers signal + hosted service in one call and replaces the default `NullOutboxWakeSignal`.

### Fixed

- CI `Pack All Projects` step now packs the new `SqlServerBroker` add-on. PostgresNotify and Locks.Redis lines are preserved.

### Deferred from v6.5.3

- **Outbox dead-letter UI surface** -> v6.5.4 (unchanged from the v6.5.2 deferral list)

### Migration from v6.5.2

Source-compatible. Add-on is opt-in: install `OrionGuard.Outbox.SqlServerBroker`, enable Service Broker on the database once, install the trigger via SQL migration, register the signal:

```csharp
services.AddSqlServerBrokerOutboxWakeSignal(o =>
{
    o.ConnectionString = "Server=db;Database=app;User Id=app;Password=app;TrustServerCertificate=true";
});
services.AddOrionGuardEfCore<AppDbContext>(opts => opts.UseOutbox());
```

## [6.5.2] - 2026-06-09

### Added

#### `Moongazing.OrionGuard.Outbox.PostgresNotify` (NEW PACKAGE)

Postgres LISTEN/NOTIFY backed `IOutboxWakeSignal` for `OrionGuard.EntityFrameworkCore`. Lifts the v6.5.1 polling-only default into an event-driven wake on every committed outbox row when the consumer's database is PostgreSQL.

- **`PostgresNotifyOutboxWakeSignal`** - `BackgroundService` that holds a dedicated `NpgsqlConnection`, runs `LISTEN "<channel>";`, and signals the dispatcher via the in-process `Channel<bool>` on every notification. Reconnect loop with exponential back-off bounded by `PostgresNotifyOptions.MaxReconnectDelay`.
- **`PostgresNotifyOptions`** - `ConnectionString` (required), `ChannelName` (default `orionguard_outbox`), `InitialReconnectDelay`, `MaxReconnectDelay`.
- **`PostgresNotifyTriggerSql`** - static helpers `Create(tableName, channelName)` / `Drop(tableName, channelName)` returning SQL that installs a `pg_notify`-emitting AFTER INSERT trigger. The package does NOT auto-install the trigger; consumers run the SQL once via an EF Core migration. Channel name is sanitised into a SQL identifier for the function / trigger names so two outbox tables in the same database do not collide.
- **DI**: `services.AddPostgresNotifyOutboxWakeSignal(o => o.ConnectionString = "...");` registers the signal + hosted service in one call, replacing the default `NullOutboxWakeSignal`.

### Fixed

- CI pack list: `Moongazing.OrionGuard.Locks.Redis` was reintroduced into the `Pack All Projects` step. The line was lost during the v6.5.0 -> v6.5.1 rebase; this restores it alongside the new `OrionGuard.Outbox.PostgresNotify` pack call so both add-on packages publish to NuGet on release.

### Deferred from v6.5.2

- **`Moongazing.OrionGuard.Outbox.SqlServerBroker`** add-on (SQL Server Service Broker push backend) -> v6.5.3
- **Outbox dead-letter UI surface** -> v6.5.4

`docs/ROADMAP.md` reflects the targets.

### Migration from v6.5.1

Source-compatible. The new add-on package is opt-in: install `OrionGuard.Outbox.PostgresNotify`, install the trigger via SQL migration, register the signal:

```csharp
services.AddPostgresNotifyOutboxWakeSignal(o =>
{
    o.ConnectionString = "Host=db;Database=app;Username=app;Password=app";
});
services.AddOrionGuardEfCore<AppDbContext>(opts => opts.UseOutbox());
```

Consumers staying on the v6.5.1 in-process channel signal or the v6.5.0 polling default see no behaviour change.

## [6.5.1] - 2026-06-04

### Added

#### Push-based dispatch contract (`Moongazing.OrionGuard.EntityFrameworkCore`)

The push-based outbox dispatcher promised in v6.5.0 lands as a contract + in-process implementation in v6.5.1. The concrete cross-process backends (Postgres `LISTEN/NOTIFY` and SQL Server Service Broker) ship as separate add-on packages in v6.5.2 and v6.5.3 respectively. The dispatcher loop now honours the new contract; consumers who do not opt in see identical v6.5.0 behaviour because the default implementation is polling-only.

- **`IOutboxWakeSignal`** abstraction in `Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push`. Two methods: `WaitForNextTickAsync(pollingInterval, ct)` (upper-bounded wait the dispatcher uses between batches) and `SignalAsync(ct)` (called by enqueue paths or push backends to wake the dispatcher immediately).
- **`NullOutboxWakeSignal`** - default registration. Polling-only; behaviour byte-for-byte identical to v6.5.0.
- **`ChannelOutboxWakeSignal`** - in-process bounded `Channel<bool>` implementation. Useful for unit tests and for single-process deployments where the `SaveChangesInterceptor` can publish a wake directly. Signals coalesce: repeated `SignalAsync` calls while one wait is pending all complete a single wake.
- **`OutboxDispatcherHostedService` constructor** gains an optional `IOutboxWakeSignal` parameter (defaults to `NullOutboxWakeSignal` when null). Polling-interval upper-bound is always honoured, so a misbehaving signal cannot stall dispatch indefinitely.
- **DI default** in `AddOrionGuardEfCore(...)` registers `NullOutboxWakeSignal` via `TryAddSingleton<IOutboxWakeSignal, NullOutboxWakeSignal>()`. Consumers replace it before `AddOrionGuardEfCore` to opt in (`services.AddSingleton<IOutboxWakeSignal, ChannelOutboxWakeSignal>()`).

### Deferred from v6.5.1

The concrete push backends will land as add-on packages so consumers can adopt one without dragging the others into their dependency graph:

- **`Moongazing.OrionGuard.Outbox.PostgresNotify`** package (Postgres `LISTEN/NOTIFY`-backed `IOutboxWakeSignal`) -> v6.5.2.
- **`Moongazing.OrionGuard.Outbox.SqlServerBroker`** package (SQL Server Service Broker-backed `IOutboxWakeSignal`) -> v6.5.3.

The outbox dead-letter UI from the original v6.5.0 plan stays at v6.5.4.

`docs/ROADMAP.md` reflects the new targets.

### Migration from v6.5.0

Source-compatible. No DI registration change is required. Consumers that opt into the new contract:

```csharp
// Single-process - in-process push so SaveChangesInterceptor can wake the dispatcher.
services.AddSingleton<IOutboxWakeSignal, ChannelOutboxWakeSignal>();
services.AddOrionGuardEfCore<AppDbContext>(o => o.UseOutbox());
```

The dispatcher continues to acquire the distributed lock before each batch, so multi-instance correctness is unchanged.

## [6.5.0] - 2026-06-01

### Added

#### `OrionGuard.Locks.Redis` (NEW PACKAGE)

- Redis backend for the `IDistributedLock` primitive introduced in v6.4.0. Bridges OrionGuard's `IDistributedLock` / `IDistributedLockHandle` to OrionLock's raw `IDistributedLockProvider`, with `RedisLockProvider` from `OrionLock.Redis` (v0.2.3) as the default wiring.
- `OrionLockBridgeDistributedLock` adapter — non-blocking acquire, owner-token release via Lua compare-and-delete on Redis. No watchdog renewal, no reentrancy. Aligns with OrionGuard's at-least-once outbox semantics where lease loss is tolerated and consumer event handlers must be idempotent.
- DI extensions on `OrionGuardEfCoreOptions`:
  - `UseOrionLockRedis(connectionString, configure)` — builds a singleton `IConnectionMultiplexer` from the supplied connection string.
  - `UseOrionLockRedis(configure)` — uses an already-registered `IConnectionMultiplexer` from DI (preferred when the application already shares a Redis connection).
- `RedisLockOptions` (re-exported via the OrionLock.Redis transitive dependency) configures `KeyPrefix` (default `orionlock:`) and `Database` (default -1).

### Changed

- `Moongazing.OrionGuard.EntityFrameworkCore` now declares `InternalsVisibleTo` for `Moongazing.OrionGuard.Locks.Redis` and its test assembly so the bridge can use the internal `OrionGuardEfCoreOptions.ServiceCustomizations` hook the same way the built-in `UseDistributedLock<T>()` does. No public API change. Other future `OrionGuard.Locks.*` backends (Consul, Postgres advisory locks, etc.) will be added to the same list when they ship.

### Deferred from v6.5.0

The original v6.5.0 milestone in `docs/ROADMAP.md` listed four features. To keep this minor focused and reviewable, two were de-scoped from v6.5.0 and re-targeted:

- **Push-based outbox dispatcher** — now targets **v6.5.1**. Will replace the v6.4 polling loop with a `PostgresLISTEN` / `SqlServerBrokerNotification` push backend on EF Core providers that support it, with a clean fallback to polling.
- **Outbox dead-letter UI surface** — now targets **v6.5.2**. A read-only `MapOutboxDashboard` endpoint listing failed/poisoned messages with replay and discard actions; authorization-required by default.

### Migration from v6.4.2

- **No breaking source changes.**
- **Adopting the Redis lock backend (multi-instance consumers who already use Redis):**
  ```csharp
  services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
  services.AddOrionGuardEfCore<AppDbContext>(opts => opts
      .UseOutbox()
      .UseOrionLockRedis(o => o.KeyPrefix = "myapp:outbox:"));
  ```
  Or with a connection string:
  ```csharp
  services.AddOrionGuardEfCore<AppDbContext>(opts => opts
      .UseOutbox()
      .UseOrionLockRedis("localhost:6379"));
  ```
- **Consumers staying on the default DB-backed `SkipLockedDistributedLock`** require no changes — it is still wired automatically by `AddOrionGuardEfCore` in Outbox mode.

## [6.4.2] - 2026-05-26

### Changed

- Logo now ships with a cream (#F7F1E3) background instead of transparent. Improves contrast against dark-mode README rendering and NuGet package card backgrounds. No functional change.

## [6.4.1] - 2026-05-23

### Changed

- New minimalist family-style logo (shield with an Orion-star center, indigo line-art, no badge ring or curved text) replaces the v6.x circular emblem. Applied to the README and to every package's NuGet icon. No code changes.

## [6.4.0] - 2026-05-20

### Added

#### Business Rule ergonomics (`Moongazing.OrionGuard`)
- `BusinessRule` and `AsyncBusinessRule` abstract base classes implementing `IBusinessRule` / `IAsyncBusinessRule`. `MessageKey` defaults to the CLR type name.
- `Guard.AgainstBrokenRule(IBusinessRule)` and `Guard.AgainstBrokenRuleAsync(IAsyncBusinessRule, CancellationToken)` static helpers. `Entity.CheckRule` / `CheckRuleAsync` now delegate to these helpers (behaviour unchanged).

#### ASP.NET Core ProblemDetails (`Moongazing.OrionGuard.AspNetCore`)
- `OrionGuardExceptionHandler` now produces a 422 `ValidationProblemDetails` for `BusinessRuleValidationException` (previously fell through to the framework default 500).
- New `OrionGuardAspNetCoreOptions.BusinessRuleStatusCode` (default 422) for clients that require 400.
- New `OrionGuardProblemDetailsFactory.Create(BusinessRuleValidationException)` overload — `errors` keyed by `RuleName`, `Type` = `https://moongazing.dev/orionguard/problems/business-rule-violation`.

#### Outbox production-hardening (`Moongazing.OrionGuard.EntityFrameworkCore`)
- `IDistributedLock` / `IDistributedLockHandle` abstractions. Default `SkipLockedDistributedLock` uses an `OutboxLock` row per key in `OrionGuard_OutboxLocks` (provider-agnostic via EF Core raw SQL). Multi-instance outbox workers no longer double-dispatch.
- `NullDistributedLock` no-op implementation for single-instance consumers who do not want to apply the new migration. Wire with `opts.UseOutbox().UseDistributedLock<NullDistributedLock>()`.
- `OutboxTypeMapRegistry` — opt-in logical-name to CLR type mapping. The `SaveChanges` interceptor prefers logical names when registered; the dispatcher resolves them on read. Falls back to AQN when no mapping exists (toggle via `OutboxTypeMapOptions.AllowAssemblyQualifiedNameFallback`).
- `OutboxArchivalHostedService` — opt-in periodic deletion of processed outbox rows. Default 30-day retention, 1-hour polling, dead-letter rows preserved.
- `OutboxOptions.LockKey` (default `"orion_guard_outbox_dispatcher"`) and `OutboxOptions.LockLeaseDuration` (default 30s).
- `OrionGuardEfCoreOptions.UseDistributedLock<T>()`, `UseOutboxTypeMap(...)`, `UseOutboxArchival(...)`.

### Changed
- `Entity.CheckRule` / `Entity.CheckRuleAsync` internally delegate to `Guard.AgainstBrokenRule` / `Guard.AgainstBrokenRuleAsync`. Public behaviour unchanged.
- `OutboxDispatcherHostedService` constructor expanded with `IDistributedLock`, `OutboxTypeMapRegistry`, and `OutboxTypeMapOptions` parameters (optional, defaulted). The DI factory in `AddOrionGuardEfCore` updates accordingly; consumers using only DI are unaffected.

### Deprecated

- The `[StronglyTypedId<TValue>]` source generator is soft-deprecated in favour of the standalone **OrionKey** package (`[OrionId<TValue>]` / `[OrionId<TValue, TStrategy>]`). Existing usages keep compiling and the generator keeps emitting; each `[StronglyTypedId]` usage now raises a CS0618 warning with migration guidance. The generator will be removed in v7.0.0. The manual `StronglyTypedId<TValue>` record, `IStronglyTypedId<TValue>`, and the related guards are unaffected. See `docs/migrations/stronglytypedid-to-orionkey.md`.

### Migration from v6.3.0

- **No breaking source changes.**
- **Distributed locking (recommended for multi-instance deployments):**
  Add an EF Core migration that creates `OrionGuard_OutboxLocks` — see `docs/migrations/v6.4.0-outbox-locks.md`.
  No code change needed when using `AddOrionGuardEfCore` — `SkipLockedDistributedLock` is wired automatically.
- **Single-instance consumers who do NOT want to apply the migration:**
  `opts.UseOutbox(...).UseDistributedLock<NullDistributedLock>()`.
- **Type-safe outbox payloads (optional):**
  `opts.UseOutbox(...).UseOutboxTypeMap(r => r.Map<UserRegistered>("user.registered"));`
- **Outbox archival (optional):**
  `opts.UseOutbox(...).UseOutboxArchival(a => a.RetentionPeriod = TimeSpan.FromDays(60));`
- **`BusinessRule` base class (optional):** existing `IBusinessRule` implementations work unchanged.
- **`Guard.AgainstBrokenRule` (additive):** `Guard.AgainstBrokenRule(new OrderMustHaveItems(order));`
- **`BusinessRuleValidationException` to 422 ProblemDetails (automatic):** customize via `OrionGuardAspNetCoreOptions.BusinessRuleStatusCode`.

### Roadmap

- v6.5+: Redis / Consul `IDistributedLock` implementations as extension packages. Push-based outbox dispatch (`LISTEN/NOTIFY`, `SqlDependency`). Audit-trail copy-before-delete for archival.

## [6.2.0] - 2026-04-19

### Added

- `Moongazing.OrionGuard.Domain.Primitives.IStronglyTypedId<TValue>` marker interface implemented by both the `StronglyTypedId<TValue>` abstract record and source-generated strongly-typed id structs.
- `Moongazing.OrionGuard.Domain.Events.DomainEventBase` abstract record — auto-assigns `EventId` (new `Guid`) and `OccurredOnUtc` (UTC timestamp) at construction, with `init` accessors for test overrides via `with` expressions.
- Source-generated strongly-typed ids now implement `IParsable<TSelf>` and `ISpanParsable<TSelf>` — ASP.NET Core minimal API route/query/form binding works out of the box.

### Changed

- `AgainstDefaultStronglyTypedId` guard receiver widened from `StronglyTypedId<TValue>` to `IStronglyTypedId<TValue>`. Source-compatible with v6.1.0 callers.
- The `[StronglyTypedId<TValue>]` source generator no longer emits its EF Core `ValueConverter` companion when the consumer project does not reference `Microsoft.EntityFrameworkCore`. JSON and TypeConverter companions emit unconditionally.
- **NuGet PackageIds for sub-packages** dropped the `Moongazing.` prefix. Install as `OrionGuard.AspNetCore`, `OrionGuard.Blazor`, `OrionGuard.Generators`, `OrionGuard.Grpc`, `OrionGuard.MediatR`, `OrionGuard.OpenTelemetry`, `OrionGuard.SignalR`, `OrionGuard.Swagger`. The v6.0.0 and v6.1.0 packages remain published under the old IDs; v6.2.0 ships under the new ones. **C# namespaces are unchanged** — `using Moongazing.OrionGuard.AspNetCore;` continues to work.

### Migration from v6.1.0

- Update package references:
  ```bash
  dotnet remove package Moongazing.OrionGuard.AspNetCore
  dotnet add package OrionGuard.AspNetCore
  ```
  Repeat for each sub-package you use. Source code (`using` statements, type names) stays the same — only the NuGet ID changes.

### Roadmap

- v6.3.0 (next): Domain event dispatcher, MediatR bridge, EF Core `SaveChanges` interceptor.
- v6.4.0: Full `BusinessRule` base class, `Guard.Against.BrokenRule`, ASP.NET Core ProblemDetails mapping.

## [6.3.0] - 2026-05-10

### Added

#### Domain event dispatcher (`Moongazing.OrionGuard.Domain.Events`)

- `IDomainEventDispatcher` and `IDomainEventHandler<TEvent>` abstractions.
- `ServiceProviderDomainEventDispatcher` — default implementation resolving handlers from `IServiceProvider`.
- `DomainEventDispatchOptions` with `DispatchMode.SequentialFailFast` (default), `SequentialContinueOnError`, and `Parallel`.
- `services.AddOrionGuardDomainEvents()` and `services.AddOrionGuardDomainEventHandlers(...)` DI helpers — idempotent (use `TryAdd*` internally) and safely composable.

#### MediatR bridge (`OrionGuard.MediatR`)

- `MediatRDomainEventDispatcher` delegates to MediatR's `IPublisher`. Consumer events opt in by adding `: INotification` to their record declaration; the bridge throws `InvalidOperationException` for events that do not. No wrapper types — handlers stay as natural `INotificationHandler<TEvent>`, so MediatR pipeline behaviours compose naturally.
- `services.AddOrionGuardMediatRDomainEvents()` swaps the registered dispatcher.

#### `OrionGuard.EntityFrameworkCore` (NEW PACKAGE)

- `DomainEventSaveChangesInterceptor` — pulls events from tracked `IAggregateRoot` instances at `SavingChangesAsync` and either dispatches them post-commit (`Inline` mode, default) or persists them as `OutboxMessage` rows in the same transaction (`Outbox` mode).
- `OutboxMessage` entity, `OutboxMessageEntityTypeConfiguration`, and `OutboxOptions` (`PollingInterval`, `BatchSize`, `MaxRetries`, `TableName`).
- `OutboxDispatcherHostedService` — `BackgroundService` that polls unprocessed rows, deserializes events, dispatches via `IDomainEventDispatcher`, increments `RetryCount` on failure, dead-letters after `MaxRetries`.
- W3C trace context propagation — outbox rows record `TraceParent` and `TraceState`; the worker resumes the parent activity context per message so end-to-end traces span the worker boundary.
- `services.AddOrionGuardEfCore<TDbContext>(o => o.UseInline() | o.UseOutbox())`.

#### `OrionGuard.Testing` (NEW PACKAGE)

- `DomainEventCapture` and `DomainEventAssertions` for fluent unit-test assertions.
- `InMemoryDomainEventDispatcher` for integration tests.
- Framework-agnostic — no xUnit / NUnit / FluentAssertions dependency. Throws `DomainEventAssertionException`, which any test runner treats as a failure.

#### `OrionGuard.OpenTelemetry`

- `OrionGuardDomainEventTelemetry` — `ActivitySource` + `Meter` under `Moongazing.OrionGuard.DomainEvents`, with `EventsDispatched`, `EventsFailed`, `OutboxProcessed`, `OutboxRetries` counters and the `DispatchDuration` histogram.
- `InstrumentedDomainEventDispatcher` decorator — opens a span per dispatch, records counters, sets activity status on exception.
- `services.WithOpenTelemetryDomainEvents()`.

#### AOT compatibility

- `ServiceProviderDomainEventDispatcher` and `OutboxDispatcherHostedService` annotated with `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`. AOT consumers should use the MediatR bridge or root event/handler types via `[DynamicDependency]`.
- All other v6.3.0 additions (`IDomainEventDispatcher`, `IDomainEventHandler<T>`, `MediatRDomainEventDispatcher`, `DomainEventCapture`, `InMemoryDomainEventDispatcher`, `InstrumentedDomainEventDispatcher`) are reflection-free.

### Migration from v6.2.0

- No breaking changes. Source-compatible.
- Existing `RaiseEvent` / `PullDomainEvents` aggregate code continues to work; events simply do not dispatch unless `AddOrionGuardDomainEvents()` is wired.
- MediatR consumers add `, INotification` to their event records (one-line per event).
- Outbox consumers add an EF Core migration for the `OrionGuard_Outbox` table.

### Roadmap

- v6.4.0: `BusinessRule` base class + `Guard.Against.BrokenRule` + ASP.NET Core ProblemDetails mapping (carries the original v6.3.0 plan); plus distributed locking for multi-instance outbox workers, `OutboxTypeMapRegistry`, archival job.
- v6.5+: Push-based outbox dispatch (`LISTEN/NOTIFY`, `SqlDependency`); event sourcing primitives.

## [6.1.0] - 2026-04-19

### Added

#### DDD Domain Primitives (`Moongazing.OrionGuard.Domain`)

- `ValueObject` abstract base class with component-wise equality via `GetEqualityComponents()`.
- `IValueObject` marker interface for record-based value objects (records get structural equality from the compiler).
- `Entity<TId>` base class with identity equality and `protected static CheckRule` / `CheckRuleAsync` helpers that throw `BusinessRuleValidationException` when a rule is broken.
- `IAggregateRoot` non-generic marker interface — enables consumers (e.g., EF Core interceptors) to discover aggregates without knowing `TId`.
- `AggregateRoot<TId>` base class with `RaiseEvent` (protected) and `PullDomainEvents` (public, atomically returns and clears the buffer).
- `StronglyTypedId<TValue>` abstract positional record (manual-use base; constraint `where TValue : notnull, IEquatable<TValue>`).

#### Abstractions for v6.2.0 / v6.3.0

- `IDomainEvent` interface (`EventId`, `OccurredOnUtc`). Dispatcher abstraction arrives in v6.2.0.
- `IBusinessRule` and `IAsyncBusinessRule` interfaces (`IsBroken`/`IsBrokenAsync`, `MessageKey`, `DefaultMessage`, optional `MessageArgs`). Full `BusinessRule` base class + `Guard.Against.BrokenRule` helpers arrive in v6.3.0.
- `BusinessRuleValidationException` — resolves messages through the existing `ValidationMessages` subsystem with fallback to `DefaultMessage`.
- `DomainInvariantException` — for raw invariant violations outside named rules.

#### `[StronglyTypedId<TValue>]` Source Generator (`OrionGuard.Generators`)

- Incremental generator using `ForAttributeWithMetadataName` with `RegisterPostInitializationOutput` to inject the attribute.
- Supported value types: `System.Guid`, `int`, `long`, `string`, `System.Ulid` (net9.0+).
- For each decorated `readonly partial struct`, emits four companion sources:
  - Partial body: `IEquatable<T>`, operators, `GetHashCode`, `ToString`, `Value` property + ctor, `New()` / `Empty` (for Guid and Ulid).
  - EF Core `ValueConverter<TId, TValue>` (namespace `Microsoft.EntityFrameworkCore.Storage.ValueConversion`).
  - `System.Text.Json.Serialization.JsonConverter<TId>` with proper per-type reader/writer methods.
  - `System.ComponentModel.TypeConverter` for ASP.NET Core route/query/form binding.

#### Guard Extensions

- `AgainstDefaultStronglyTypedId<TValue>(this StronglyTypedId<TValue> id, ...)` — throws `NullValueException` when `id` is null or `ZeroValueException` when its wrapped value equals the default of `TValue` (including empty string).

#### Dependency Injection

- `services.AddOrionGuardStronglyTypedIds(params Assembly[] assemblies)` — scans assemblies for source-generated `*EfCoreValueConverter` types and registers each as a singleton.

#### Localization

- 3 new keys added to all 14 bundled languages (42 new translations):
  - `DefaultStronglyTypedId`
  - `BusinessRuleBroken`
  - `DomainInvariantViolated`

#### Benchmarks

- `DomainPrimitivesBenchmark` — compares `ValueObject` class equality vs record equality, and measures `AggregateRoot.RaiseEvent` + `PullDomainEvents` overhead on net8.0 and net9.0.

### Notes

- The DDD toolkit is the first of a three-phase rollout. v6.2.0 will add the domain-event dispatcher + MediatR bridge + EF Core `SaveChanges` interceptor. v6.3.0 will add the full `BusinessRule` base class, `Guard.Against.BrokenRule`, `Validate.Rule` / `Validate.Rules`, and ASP.NET Core `BusinessRuleValidationException` → RFC 9457 ProblemDetails mapping.
- No new NuGet packages — all additions land in existing packages (`Moongazing.OrionGuard` core, `OrionGuard.Generators`).

## [6.0.0] - 2026-04-05

### Added

#### GeneratedRegex Migration
- All 24 regex patterns migrated to .NET 8+ `[GeneratedRegex]` for NativeAOT compatibility. Zero runtime compilation overhead.

#### 14-Language Localization
- Added Chinese (zh), Korean (ko), Russian (ru), Dutch (nl), Polish (pl).
- Completed all 30 message keys for German, French, Spanish, Portuguese, Arabic, Japanese.
- Total: 14 languages x 30 keys = 420 messages.

#### Rate Limit Guards
- `AgainstRateLimitExceeded()` -- general rate limit validation.
- `AgainstTooManyRequests()` -- HTTP 429-style request throttling.
- `AgainstSlidingWindowExceeded()` -- sliding window rate limit check.
- `AgainstConcurrentLimitExceeded()` -- concurrent request limit validation.
- `AgainstDailyQuotaExceeded()` -- daily quota enforcement.
- New `RateLimitExceededException` exception type.

#### International Guards
- `AgainstInvalidSwiftCode()` -- SWIFT/BIC code validation.
- `AgainstInvalidIsbn()` -- ISBN-10/ISBN-13 validation.
- `AgainstInvalidVin()` -- Vehicle Identification Number validation.
- `AgainstInvalidEan()` -- European Article Number (barcode) validation.
- `AgainstInvalidVatNumber()` -- VAT number format validation.
- `AgainstInvalidImei()` -- IMEI device identifier validation.

#### Business Guards
- `AgainstExpired()` -- token/subscription/license expiration check.
- `AgainstNotYetActive()` -- validates that an activation date has been reached.

#### Dynamic Rule Engine
- JSON-configurable runtime validation with 14 rule types: NotNull, NotEmpty, Length, Range, Email, Regex, In, NotIn, and more.
- `DynamicValidator.FromJson()` for loading rules from JSON configuration.
- `DynamicValidatorFactory` for creating validators from rule definitions.

#### Custom Exception Factory
- `IExceptionFactory` interface for pluggable exception creation.
- `DefaultExceptionFactory` implementation.
- `ExceptionFactoryProvider` for registering and resolving custom factories.

#### Deep Nested Validation
- `Validate.Nested(obj)` with unlimited depth traversal.
- `.Nested()` for validating child objects.
- `.Collection()` for validating collection items with indexed paths (e.g., `Items[0].Name`).

#### Cross-Property DSL
- `Validate.CrossProperties(obj)` entry point.
- `.AreEqual()`, `.AreNotEqual()` -- property equality comparisons.
- `.IsGreaterThan()`, `.IsLessThan()` -- relational comparisons.
- `.AtLeastOneRequired()` -- ensures at least one of the specified properties has a value.

#### Polymorphic Validation
- `Validate.Polymorphic<TBase>()` with `.When<TDerived>()` for type-discriminated validation rules.

#### Validation Result Caching
- `CachedValidator<T>` decorator with configurable TTL.
- `.WithCaching()` extension method for wrapping any validator.

#### RuleSets
- `RuleSet("create", () => ...)` for grouping validation rules.
- Execute selectively: `validator.Validate(obj, RuleSet.Create)`.

#### IRequestValidator<T>
- Pipeline-ready validator interface for middleware/MediatR integration.

#### GuardResult.SuggestedHttpStatusCode
- HTTP status code hints on `GuardResult` for ProblemDetails mapping.

### New Packages

#### Moongazing.OrionGuard.AspNetCore
- Validation middleware and `[ValidateRequest]` attribute.
- `.WithValidation<T>()` Minimal API filter.
- MVC action filter for automatic model validation.
- RFC 9457 ProblemDetails response formatting.
- `IExceptionHandler` integration.
- IOptions validation with `.ValidateWithOrionGuard()`.

#### Moongazing.OrionGuard.MediatR
- `ValidationBehavior<TRequest, TResponse>` pipeline behavior.
- Assembly scanning for automatic validator registration.

#### Moongazing.OrionGuard.Generators
- `[GenerateValidator]` source generator for compile-time, reflection-free, NativeAOT-compatible validation.

#### Moongazing.OrionGuard.Swagger
- `OrionGuardSchemaFilter` for automatic OpenAPI constraint generation from validation attributes.

#### Moongazing.OrionGuard.OpenTelemetry
- `InstrumentedValidator<T>` decorator with metrics (total/failures/duration) and distributed tracing.

#### Moongazing.OrionGuard.Blazor
- `<OrionGuardValidator />` EditForm component.
- `<OrionGuardFluentValidator TModel="..." />` EditForm component.

#### Moongazing.OrionGuard.Grpc
- `OrionGuardInterceptor` server interceptor with streaming support.

#### Moongazing.OrionGuard.SignalR
- `OrionGuardHubFilter` for automatic hub method parameter validation.

### Deprecated

- `RegexPatterns` class -- use `GeneratedRegexPatterns` instead. Will be removed in v7.0.

### Internal

- Benchmark suite with BenchmarkDotNet (NullCheck, Email, Regex, Security, ObjectValidator comparisons).

---

## [5.0.0] - 2026-04-02

### Breaking Changes

- All exception classes are now `sealed` and include `ErrorCode` and `ParameterName` properties.
- `Validate.Object<T>()` renamed to `Validate.For<T>()`.
- `Validate.ObjectStrict<T>()` renamed to `Validate.ForStrict<T>()`.
- `FastGuard.Guid()` renamed to `FastGuard.ValidGuid()`.
- Removed `TurkishGuards` -- replaced by the universal `FormatGuards` class.
- `AgainstEmptyCollection` now throws `NullValueException` instead of `EmptyStringException` (bug fix).
- `AgainstNotAllLowercase` behavior corrected -- previously always passed due to a self-comparison bug.

### Added

#### ThrowHelper Pattern
- New `ThrowHelper` static class with `[DoesNotReturn]` and `[StackTraceHidden]` attributes.
- All hot-path guard methods delegate throwing to ThrowHelper for smaller JIT-compiled method bodies.

#### Span-Based FastGuard Methods
- `FastGuard.Email(string, string)` -- validates email format using zero-allocation span parsing.
- `FastGuard.Ascii(ReadOnlySpan<char>, string)` -- ensures all characters are within the ASCII range.
- `FastGuard.AlphaNumeric(ReadOnlySpan<char>, string)` -- rejects non-alphanumeric characters.
- `FastGuard.NumericString(ReadOnlySpan<char>, string)` -- digits-only span validation.
- `FastGuard.MaxLength(ReadOnlySpan<char>, int, string)` -- maximum length check on spans.
- `FastGuard.ValidGuid(ReadOnlySpan<char>, string)` -- GUID format and non-empty check.
- `FastGuard.Finite(double, string)` -- rejects NaN and Infinity.

#### Security Guards (new file)
- `AgainstSqlInjection` -- detects 28 common SQL injection patterns using a `FrozenSet`.
- `AgainstXss` -- detects 28 cross-site scripting vectors including event handlers and DOM sinks.
- `AgainstPathTraversal` -- catches directory traversal sequences and common encoded variants.
- `AgainstCommandInjection` -- blocks shell metacharacters, pipe operators, and known interpreters.
- `AgainstLdapInjection` -- span-based detection of LDAP-special characters.
- `AgainstXxe` -- detects DOCTYPE and ENTITY declarations indicative of XXE attacks.
- `AgainstInjection` -- combined check that runs SQL, XSS, path traversal, and command injection in one call.
- `AgainstUnsafeFileName` -- validates filenames against path traversal and invalid OS characters.
- `AgainstOpenRedirect` -- validates redirect URLs against an allow-list of trusted domains.

#### Format Guards (new file, replaces TurkishGuards)
- `AgainstInvalidLatitude` / `AgainstInvalidLongitude` / `AgainstInvalidCoordinates` -- geographic coordinate validation.
- `AgainstInvalidMacAddress` -- validates MAC addresses in colon, hyphen, or dot-separated notation.
- `AgainstInvalidHostname` -- RFC 1123 hostname validation including label length and character rules.
- `AgainstInvalidCidr` -- validates CIDR notation for both IPv4 and IPv6 addresses.
- `AgainstInvalidCountryCode` -- checks against the full ISO 3166-1 alpha-2 set (249 codes).
- `AgainstInvalidTimeZoneId` -- validates against the system's IANA/Windows time zone database.
- `AgainstInvalidLanguageTag` -- BCP 47 / IETF language tag validation using CultureInfo.
- `AgainstInvalidJwtFormat` -- structural validation of JWT tokens (three Base64URL segments).
- `AgainstInvalidConnectionString` -- checks for well-formed key=value connection string pairs.
- `AgainstInvalidBase64String` -- validates Base64 encoding structure and padding.

#### ObjectValidator Enhancements
- Compiled expression caching via `ConcurrentDictionary` to avoid repeated `Expression.Compile()` overhead.
- `CrossProperty<TProp1, TProp2>()` for cross-property validation with a custom predicate.
- `When(bool, Action<ObjectValidator<T>>)` for conditional validation blocks.

#### FluentGuard Enhancements
- `Transform(Func<T, T>)` -- in-pipeline value transformation (trim, lowercase, etc.).
- `Default(T)` -- replaces null values with a specified default during validation.
- All date comparisons now use `DateTime.UtcNow`.

#### Thread-Safe Localization
- `ValidationMessages` rewritten with `ConcurrentDictionary` and `AsyncLocal<CultureInfo>`.
- `SetCultureForCurrentScope(CultureInfo)` for per-request culture without affecting other threads.
- Expanded to 8 languages: English, Turkish, German, French, Spanish, Portuguese, Arabic, Japanese.
- 30 message keys per major language with fallback to English for missing entries.

#### Infrastructure
- `GuardProfileRegistry` now uses `ConcurrentDictionary` for thread-safe profile registration.
- Added `TryExecute<T>()`, `IsRegistered()`, and `Remove()` methods to `GuardProfileRegistry`.
- `RegexCache` with bounded size (`MaxCacheSize = 1000`) replaces all raw `Regex.IsMatch` calls.
- CI/CD workflow added: builds and tests on every push, publishes to NuGet.org and GitHub Packages on release.

### Changed

- All `DateTime.Now` references replaced with `DateTime.UtcNow` across DateTimeGuards and FluentGuard.
- All `Regex.IsMatch()` calls replaced with `RegexCache.IsMatch()` (StringGuards, AdvancedStringGuards, BusinessGuards).
- `BusinessGuards` currency codes now stored in a static `FrozenSet<string>` instead of allocating a new `HashSet` per call.
- `CollectionGuards.AgainstExceedingCount` optimized: checks `ICollection<T>.Count` or `IReadOnlyCollection<T>.Count` first, falls back to `Take(n+1).Count()` for unenumerated sequences.
- `FileGuards.AgainstInvalidFileExtension` now uses `StringComparer.OrdinalIgnoreCase` for extension matching.
- All `StartsWith` / `EndsWith` calls include an explicit `StringComparison` parameter.
- `AnalysisLevel` set to `latest-recommended` with `TreatWarningsAsErrors` enabled.
- `GeneratePackageOnBuild` set to `false` (packing handled by CI pipeline).

### Fixed

- `AgainstNotAllLowercase` was comparing the string to itself with `InvariantCultureIgnoreCase`, which always returned true. Now compares against `ToLowerInvariant()` with `Ordinal` comparison.
- `AgainstEmptyCollection` was throwing `EmptyStringException` for empty collections. Now correctly throws `NullValueException`.
- `GuardBuilderExtensions` was passing `.Value` instead of `.ParameterName` in error messages.
- Missing `using System.Diagnostics` in ThrowHelper and FastGuard caused `StackTraceHidden` build errors.
- Multiple CA analyzer violations resolved: CA1305, CA1310, CA1720, CA1845, CA1862, CA2263.

### Removed

- `TurkishGuards.cs` -- country-specific validations removed in favor of universal `FormatGuards`.

---

## [4.0.0] - 2025-01-XX

### ?? Major Features

#### New Fluent API with `Ensure.That()`
```csharp
// Automatic parameter name capture with CallerArgumentExpression
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(password).NotNull().MinLength(8).Matches(@"^(?=.*[A-Z])");

// Shorthand methods
string validEmail = Ensure.NotNull(email);
string validName = Ensure.NotNullOrEmpty(name);
int validAge = Ensure.InRange(age, 18, 120);
```

#### Result Pattern with Error Accumulation
```csharp
// Collect all errors instead of throwing on first
var result = GuardResult.Combine(
    Ensure.Accumulate(email, "Email").NotNull().Email().ToResult(),
    Ensure.Accumulate(password, "Password").MinLength(8).ToResult(),
    Ensure.Accumulate(username, "Username").Length(3, 30).ToResult()
);

if (result.IsInvalid)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"[{error.ParameterName}]: {error.Message}");
    }
}

// API-friendly error format
Dictionary<string, string[]> errors = result.ToErrorDictionary();
```

#### Async Validation Support
```csharp
var result = await EnsureAsync.That(email, "Email")
    .UniqueAsync(async e => await userRepository.IsEmailUniqueAsync(e))
    .ExistsAsync(async e => await emailService.IsDeliverableAsync(e))
    .ValidateAsync();
```

#### Conditional Validation (When/Unless)
```csharp
Ensure.That(secondaryEmail)
    .When(isPrimaryEmailInvalid)     // Only validate when condition is true
    .NotNull()
    .Email()
    .Unless(isGuestUser)             // Skip when condition is true
    .MinLength(5)
    .Always()                        // Reset - always validate from here
    .NotEmpty();
```

#### Object Validation with Property Expressions
```csharp
var result = Validate.Object(userDto)
    .Property(u => u.Email, g => g.NotNull().Email())
    .Property(u => u.Password, g => g.NotNull().MinLength(8))
    .Property(u => u.Age, g => g.InRange(18, 120))
    .NotNull(u => u.CreatedAt)
    .Must(u => u.Role, r => r != "Admin", "Cannot self-assign admin role")
    .ToResult();
```

#### Code Contracts
```csharp
public decimal CalculateDiscount(decimal price, decimal discountPercent)
{
    // Preconditions
    Contract.Requires(price >= 0, "Price must be non-negative");
    Contract.Requires(discountPercent >= 0 && discountPercent <= 100, "Invalid discount");
    
    var result = price * (1 - discountPercent / 100);
    
    // Postcondition
    Contract.Ensures(result >= 0 && result <= price, "Result must be valid");
    
    return result;
}
```

#### High-Performance Guards (FastGuard)
```csharp
// Optimized for hot paths with aggressive inlining
FastGuard.NotNullOrEmpty(email, nameof(email));
FastGuard.InRange(age, 0, 150, nameof(age));
FastGuard.Positive(quantity, nameof(quantity));

// Span-based validation for zero allocations
FastGuard.NotEmpty(dataSpan, nameof(dataSpan));
```

#### Logical Guards (AND/OR)
```csharp
// OR logic - any condition passing is sufficient
var phoneOrEmail = contact.EitherOr("Contact")
    .Or(c => IsValidEmail(c), "valid email")
    .Or(c => IsValidPhone(c), "valid phone")
    .Validate();

// AND logic with short-circuit
var result = value.AllOf("Value", shortCircuit: true)
    .And(v => v != null, "cannot be null")
    .And(v => v.Length > 0, "cannot be empty")
    .And(v => v.Length < 100, "too long")
    .ToResult();
```

### ?? New Features

#### Advanced String Validators
- **Credit Card** - Luhn algorithm validation (Visa, MasterCard)
- **IBAN** - International Bank Account Number
- **JSON/XML** - Structure validation
- **Base64** - Encoding validation
- **Turkish ID (TC Kimlik)** - National ID validation
- **Hex Color** - Color code validation
- **Semantic Version** - SemVer format
- **URL Slug** - URL-friendly format

```csharp
"4111111111111111".AgainstInvalidCreditCard("card");
"DE89370400440532013000".AgainstInvalidIban("iban");
"10000000146".AgainstInvalidTurkishId("tcNo");
"{\"key\": \"value\"}".AgainstInvalidJson("data");
"#FF5733".AgainstInvalidHexColor("color");
"1.2.3-beta.1".AgainstInvalidSemVer("version");
```

#### Business Domain Guards
```csharp
// Money & Currency
amount.AgainstInvalidMonetaryAmount("price", maxDecimalPlaces: 2);
"TRY".AgainstInvalidCurrencyCode("currency");
discount.AgainstInvalidPercentage("discount");

// E-commerce
total.AgainstOrderBelowMinimum(minimumOrder: 50m, "orderTotal");
"SUMMER2025".AgainstInvalidCouponCode("coupon");
"SKU-12345-XL".AgainstInvalidSku("productSku");

// Business Hours
orderDate.AgainstWeekend("deliveryDate");
appointmentTime.AgainstOutsideBusinessHours("appointment", startHour: 9, endHour: 18);

// Rating & Review
rating.AgainstInvalidRating("stars", minRating: 1, maxRating: 5);
reviewText.AgainstInvalidReviewText("review", minLength: 10, maxLength: 5000);
```

#### Validation Attributes
```csharp
public class CreateUserRequest
{
    [NotNull, Email]
    public string Email { get; set; }
    
    [NotEmpty, Length(8, 128)]
    public string Password { get; set; }
    
    [Range(18, 120)]
    public int Age { get; set; }
    
    [Regex(@"^\+?[1-9]\d{1,14}$")]
    public string? Phone { get; set; }
}

// Validate using attributes
var result = AttributeValidator.Validate(request);
```

#### Dependency Injection Integration
```csharp
// Register in Startup/Program.cs
services.AddOrionGuard();
services.AddValidator<CreateUserRequest, CreateUserRequestValidator>();

// Create custom validator
public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email, "Email", v => v.NotNull().NotEmpty().Email());
        RuleFor(x => x.Password, "Password", v => v.NotNull().MinLength(8));
        RuleForAsync(async x => await IsEmailUnique(x.Email), "Email already exists", "Email");
    }
}

// Use in controller
public class UsersController
{
    private readonly IValidator<CreateUserRequest> _validator;
    
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var result = await _validator.ValidateAsync(request);
        if (result.IsInvalid)
            return BadRequest(result.ToErrorDictionary());
        // ...
    }
}
```

#### Localization Support
```csharp
// Set culture globally
ValidationMessages.SetCulture("tr");

// Get localized messages
var message = ValidationMessages.Get("NotNull", "Email");
// TR: "Email bo? olamaz."
// EN: "Email cannot be null."
// DE: "Email darf nicht null sein."

// Add custom translations
ValidationMessages.AddMessages("es", new Dictionary<string, string>
{
    ["NotNull"] = "{0} no puede ser nulo.",
    ["Email"] = "{0} debe ser una direcci�n de correo v�lida."
});
```

#### Common Validation Profiles
```csharp
var emailResult = CommonProfiles.Email("user@example.com");
var passwordResult = CommonProfiles.Password("SecureP@ss1", 
    minLength: 8, 
    requireUppercase: true,
    requireSpecialChar: true);
var usernameResult = CommonProfiles.Username("john_doe", minLength: 3, maxLength: 30);
var phoneResult = CommonProfiles.PhoneNumber("+905551234567");
var birthDateResult = CommonProfiles.BirthDate(birthDate, minAge: 18, maxAge: 120);
var moneyResult = CommonProfiles.MonetaryAmount(99.99m, min: 0, max: 10000);
```

### ? Performance Improvements

- **Regex Caching**: Compiled regex patterns are cached for reuse
- **Span-based Operations**: Zero-allocation validation for hot paths
- **Aggressive Inlining**: Critical paths optimized with `MethodImplOptions.AggressiveInlining`
- **Short-circuit Evaluation**: Optional early exit on first failure
- **Debug Guards**: Conditional compilation for development-only assertions

### ?? Breaking Changes

- Minimum target framework changed to .NET 8.0 (still supports .NET 9.0)
- `Guard.For<T>()` now returns `GuardBuilder<T>` for legacy compatibility
- New `Ensure.That<T>()` is the recommended entry point for v4.0

### ?? Package Changes

- Added dependency: `Microsoft.Extensions.DependencyInjection.Abstractions`
- Multi-targeting: net8.0, net9.0

### ?? New Files Added

```
src/Moongazing.OrionGuard/
??? Core/
?   ??? Ensure.cs              # New fluent API entry point
?   ??? FluentGuard.cs         # Enhanced fluent builder
?   ??? GuardResult.cs         # Result pattern implementation
?   ??? AsyncGuard.cs          # Async validation support
?   ??? ObjectValidator.cs     # Object property validation
?   ??? Contract.cs            # Code contracts
?   ??? FastGuard.cs           # High-performance guards
?   ??? LogicalGuards.cs       # AND/OR logic
??? Attributes/
?   ??? ValidationAttributes.cs # Attribute-based validation
??? DependencyInjection/
?   ??? ServiceCollectionExtensions.cs
??? Extensions/
?   ??? AdvancedStringGuards.cs # Credit card, IBAN, etc.
?   ??? BusinessGuards.cs       # Domain-specific guards
??? Localization/
?   ??? ValidationMessages.cs   # Multi-language support
??? Profiles/
    ??? CommonProfiles.cs       # Pre-built validation profiles
```

---

## [3.0.0] - Previous Release

- Initial fluent API with `Guard.For<T>()`
- Basic validation profiles
- Extension methods for common types
- Custom exception types

---

## Migration Guide (3.x ? 4.0)

### Recommended Changes

```csharp
// Before (v3.x) - Still works!
Guard.For(email, nameof(email)).NotNull().NotEmpty();

// After (v4.0) - Recommended
Ensure.That(email).NotNull().NotEmpty();
```

### New Error Handling Pattern

```csharp
// Before (v3.x)
try {
    Guard.For(email, "email").Email();
} catch (InvalidEmailException ex) {
    // Handle
}

// After (v4.0) - Result pattern
var result = Ensure.Accumulate(email, "email").Email().ToResult();
if (result.IsInvalid) {
    return BadRequest(result.ToErrorDictionary());
}
```

---

## Authors

- **Tunahan Ali Ozturk** - *Creator & Maintainer*

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE.txt) file for details.
