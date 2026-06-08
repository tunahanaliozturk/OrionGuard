# Changelog

All notable changes to OrionGuard will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
