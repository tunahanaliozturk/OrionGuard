# OrionGuard v6.4.0 — Business Rule helpers, Outbox distributed locking, type map & archival

**Date:** 2026-05-19
**Status:** Approved (design); pending implementation plan
**Branch:** `feature/v6.4.0`
**Predecessor:** v6.3.0 (`IDomainEventDispatcher`, MediatR bridge, transactional outbox single-instance)

## 1. Goal

Ship the five features promised in the v6.3.0 → v6.4.0 roadmap as a single source-compatible minor release:

**Business Rule ergonomics (carried over from the original v6.3.0 plan):**
1. `BusinessRule` / `AsyncBusinessRule` abstract base classes for `IBusinessRule` / `IAsyncBusinessRule`.
2. `Guard.AgainstBrokenRule(rule)` / `Guard.AgainstBrokenRuleAsync(rule, ct)` helpers.
3. ASP.NET Core ProblemDetails mapping for `BusinessRuleValidationException` (currently produces a generic 500 because the exception handler does not match it).

**Outbox production-hardening:**
4. Pluggable `IDistributedLock` abstraction with a default DB-backed implementation so multi-instance outbox workers stop double-dispatching events.
5. `OutboxTypeMapRegistry` — opt-in logical-name → CLR-type mapping so outbox payloads survive type renames and AOT consumers can avoid `Type.GetType` reflection. Archival hosted service for retention-based cleanup of processed rows.

**Non-goals (deferred):**
- Redis/Consul `IDistributedLock` implementations — `Moongazing.OrionGuard.Redis` would be a v6.5+ extension package; no concrete consumer demand today.
- Push-based outbox dispatch (`LISTEN/NOTIFY`, `SqlDependency`) — v6.5+.
- Event sourcing primitives — v6.5+.
- Archival → audit-trail table (copy-before-delete) — v6.5+.

## 2. Package layout

| # | Feature | Package | New files | Modified files |
|---|---|---|---|---|
| 1 | `BusinessRule` / `AsyncBusinessRule` | `OrionGuard` (core) | `Domain/Rules/BusinessRule.cs`, `Domain/Rules/AsyncBusinessRule.cs` | — |
| 2 | `Guard.AgainstBrokenRule` | `OrionGuard` (core) | — | `Core/Guard.cs`, `Domain/Primitives/Entity.cs` (delegation) |
| 3 | ProblemDetails for `BusinessRuleValidationException` | `OrionGuard.AspNetCore` | — | `ExceptionHandling/OrionGuardExceptionHandler.cs`, `ProblemDetails/OrionGuardProblemDetailsFactory.cs`, `Options/OrionGuardAspNetCoreOptions.cs` |
| 4 | `IDistributedLock` abstraction + DB impl + null impl | `OrionGuard.EntityFrameworkCore` | `Outbox/Locking/IDistributedLock.cs`, `Outbox/Locking/IDistributedLockHandle.cs`, `Outbox/Locking/SkipLockedDistributedLock.cs`, `Outbox/Locking/NullDistributedLock.cs`, `Outbox/Locking/OutboxLock.cs`, `Outbox/Locking/OutboxLockEntityTypeConfiguration.cs` | `Outbox/OutboxDispatcherHostedService.cs`, `Outbox/OutboxOptions.cs`, `OrionGuardEfCoreOptions.cs`, `ServiceCollectionExtensions.cs` |
| 5 | `OutboxTypeMapRegistry` | `OrionGuard.EntityFrameworkCore` | `Outbox/TypeMap/OutboxTypeMapRegistry.cs`, `Outbox/TypeMap/OutboxTypeMapOptions.cs` | `Outbox/OutboxDispatcherHostedService.cs`, outbox writer code path |
| 6 | Archival hosted service | `OrionGuard.EntityFrameworkCore` | `Outbox/Archival/OutboxArchivalHostedService.cs`, `Outbox/Archival/OutboxArchivalOptions.cs` | DI helpers |

**No new NuGet packages.** Pluggable `IDistributedLock` lives next to the default implementation in `OrionGuard.EntityFrameworkCore`. A future `OrionGuard.Redis` extension package can take a dependency on `OrionGuard.EntityFrameworkCore` solely for the interface; the abstraction does not require a separate carrier package today. (YAGNI — extract later when concrete demand arrives.)

## 3. Business Rule base classes (`OrionGuard`)

### 3.1 New types

```csharp
namespace Moongazing.OrionGuard.Domain.Rules;

public abstract class BusinessRule : IBusinessRule
{
    public abstract bool IsBroken();
    public abstract string DefaultMessage { get; }
    public virtual string MessageKey => GetType().Name;
    public virtual object[]? MessageArgs => null;
}

public abstract class AsyncBusinessRule : IAsyncBusinessRule
{
    public abstract Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default);
    public abstract string DefaultMessage { get; }
    public virtual string MessageKey => GetType().Name;
    public virtual object[]? MessageArgs => null;
}
```

### 3.2 Why default `MessageKey => GetType().Name`

The existing `BusinessRuleValidationException` resolves messages through `ValidationMessages.Get(key)` and falls back to `DefaultMessage` when no translation is registered. Defaulting `MessageKey` to the CLR type name (e.g., `"OrderMustHaveItems"`) gives consumers a least-surprise binding between rule class and resource key. Consumers who prefer snake_case or domain-prefixed keys override the property.

### 3.3 `IsBroken` and `DefaultMessage` are abstract

`IsBroken` is the entire contract — leaving it abstract is non-negotiable.
`DefaultMessage` is abstract (not virtual with a generic fallback) so consumers cannot accidentally ship a rule with no human-readable description when no resource translation exists.

### 3.4 Existing `IBusinessRule` consumers are unaffected

`BusinessRule` is additive — it implements `IBusinessRule`. Existing interface implementations continue to satisfy `Entity.CheckRule`, `Guard.AgainstBrokenRule`, and `BusinessRuleValidationException` without changes.

## 4. `Guard.AgainstBrokenRule` helpers (`OrionGuard`)

### 4.1 New surface

```csharp
public static partial class Guard
{
    public static void AgainstBrokenRule(IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.IsBroken())
        {
            throw new BusinessRuleValidationException(rule);
        }
    }

    public static async Task AgainstBrokenRuleAsync(
        IAsyncBusinessRule rule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (await rule.IsBrokenAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessRuleValidationException(rule);
        }
    }
}
```

### 4.2 Naming follows the existing Guard surface

The current Guard surface uses `Guard.AgainstNull`, `Guard.AgainstNullOrEmpty` (static), and value extensions for primitives (`value.AgainstNegative`). A rule is self-contained — not "applied to a value" — so `Guard.AgainstBrokenRule(rule)` is a static call, matching `AgainstNull`. The roadmap entry "`Guard.Against.BrokenRule`" is interpreted as a naming pattern rather than a literal C# chain. Adopting the Ardalis.GuardClauses-style `Guard.Against.X` chain would require redesigning the entire Guard surface and would be a v7.0.0 break.

### 4.3 `Entity.CheckRule` delegates to `Guard.AgainstBrokenRule`

`Entity.CheckRule` and `Entity.CheckRuleAsync` currently contain the same null check + IsBroken check + throw logic. v6.4.0 rewrites them as one-line delegations to the new Guard helpers. Single source of truth; future telemetry/decorator hooks land in one place. Public behaviour is identical; existing tests continue to pass.

## 5. ProblemDetails for `BusinessRuleValidationException` (`OrionGuard.AspNetCore`)

### 5.1 The current gap

`OrionGuardExceptionHandler.TryHandleAsync` matches `AggregateValidationException` and `GuardException` and returns `true` for both. `BusinessRuleValidationException` does not match either branch, so it falls through to the ASP.NET Core default handler — typically a `500 Internal Server Error` ProblemDetails with no rule context.

### 5.2 Handler change

Add a third branch between the aggregate and the guard branches:

```csharp
if (exception is BusinessRuleValidationException ruleException)
{
    _logger.LogWarning(
        ruleException,
        "Business rule '{RuleName}' violated: {Message}",
        ruleException.RuleName,
        ruleException.Message);

    var statusCode = _options.BusinessRuleStatusCode;

    if (_options.UseProblemDetails)
    {
        var problemDetails = OrionGuardProblemDetailsFactory.Create(ruleException);
        problemDetails.Status = statusCode;
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = MediaTypeNames.Application.Json;
        await httpContext.Response.WriteAsJsonAsync(
            problemDetails, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }
    else
    {
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = MediaTypeNames.Application.Json;
        await httpContext.Response.WriteAsJsonAsync(
            new { ruleName = ruleException.RuleName, message = ruleException.Message },
            SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    return true;
}
```

Branch order in the handler:
1. `AggregateValidationException` (most specific — aggregate of multiple validation errors).
2. `BusinessRuleValidationException` (**new**).
3. `GuardException` (broadest single-parameter failure).

The three exception hierarchies are disjoint, so order is not behaviourally significant; this order reads as validation → rule → guard, which mirrors call depth.

### 5.3 Factory addition

```csharp
public static ValidationProblemDetails Create(BusinessRuleValidationException exception)
{
    var errors = new Dictionary<string, string[]>
    {
        [exception.RuleName] = [exception.Message],
    };

    return new ValidationProblemDetails(errors)
    {
        Type = "https://moongazing.dev/orionguard/problems/business-rule-violation",
        Title = "Business Rule Violation",
        Status = StatusCodes.Status422UnprocessableEntity,
    };
}
```

### 5.4 Options

```csharp
public sealed class OrionGuardAspNetCoreOptions
{
    // ...existing properties

    /// <summary>
    /// HTTP status code returned for <see cref="BusinessRuleValidationException"/>.
    /// Default 422 Unprocessable Entity (RFC 9457). Override to 400 for clients that require it.
    /// </summary>
    public int BusinessRuleStatusCode { get; set; } = StatusCodes.Status422UnprocessableEntity;
}
```

### 5.5 Why 422 by default

Request bodies and bindings are syntactically valid by the time a domain rule fires — the failure is semantic. RFC 9457 / REST consensus map this to `422 Unprocessable Entity`. `400 Bad Request` is reserved for syntactic/parsing failures (which is where `GuardException` already lands, 400).

### 5.6 Why a custom `Type` URL

The default `https://tools.ietf.org/html/rfc9457` URL applies to *any* ProblemDetails. Clients that key off `Type` to switch error-handling strategies can use `https://moongazing.dev/orionguard/problems/business-rule-violation` to distinguish business-rule violations from other validation failures without inspecting the body. The URL does not need to resolve; a follow-up `docs/problems/business-rule-violation.md` page is recommended but out of scope for this release.

## 6. `IDistributedLock` abstraction + DB implementation (`OrionGuard.EntityFrameworkCore`)

### 6.1 The current race

`OutboxDispatcherHostedService.ProcessBatchAsync` issues an unguarded `WHERE ProcessedOnUtc IS NULL ORDER BY OccurredOnUtc LIMIT N`. Two workers on the same database get the same N rows and double-dispatch every event. The class-level XML comment and the startup warning currently acknowledge this with "distributed locking lands in v6.4."

### 6.2 Abstraction

```csharp
namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

public interface IDistributedLock
{
    Task<IDistributedLockHandle?> TryAcquireAsync(
        string lockKey,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
}

public interface IDistributedLockHandle : IAsyncDisposable
{
    string LockKey { get; }
}
```

**Non-blocking semantics:** `TryAcquireAsync` returns `null` immediately if the lock is held. Callers (dispatcher, archival) skip the iteration and try again on the next polling tick. Blocking-acquire APIs in worker loops hide contention behind timeouts.

**Lease-based:** the owner declares how long they intend to hold the lock. Lease expiry releases the lock even if the owner crashes without disposing the handle.

### 6.3 Default DB implementation — `SkipLockedDistributedLock`

#### 6.3.1 Why a dedicated `OutboxLock` table rather than `SELECT ... FOR UPDATE SKIP LOCKED` on `OutboxMessage`

| | `OutboxLock` table | `FOR UPDATE SKIP LOCKED` on rows |
|---|---|---|
| Abstraction shape | Named lock, semantic for any worker | Row-level fetch+lock combined |
| Provider support | EF Core raw SQL works on PG/MSSQL/MySQL/SQLite | PG/MSSQL/MySQL; SQLite no |
| Test surface | SQLite in-memory integration tests work | Tests must run against a real PG/MSSQL |
| Coordination with archival | Same locking primitive, different key | Each job invents its own |
| New consumer migration | Yes (`OrionGuard_OutboxLocks` table) | Yes (none — but tests need a real DB) |

The named-lock model also keeps `IDistributedLock` semantically coherent — a hypothetical Redis implementation maps `lockKey` directly to a Redis key; a `SKIP LOCKED` interpretation wouldn't.

#### 6.3.2 Schema

```csharp
public sealed class OutboxLock
{
    public string LockKey { get; set; } = default!;      // PK, max length 200
    public Guid? HolderId { get; set; }                  // null when free
    public DateTime AcquiredOnUtc { get; set; }
    public DateTime ExpiresOnUtc { get; set; }
}
```

Configuration:

```csharp
public sealed class OutboxLockEntityTypeConfiguration : IEntityTypeConfiguration<OutboxLock>
{
    public void Configure(EntityTypeBuilder<OutboxLock> builder)
    {
        builder.ToTable("OrionGuard_OutboxLocks");
        builder.HasKey(x => x.LockKey);
        builder.Property(x => x.LockKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.HolderId);
        builder.Property(x => x.AcquiredOnUtc);
        builder.Property(x => x.ExpiresOnUtc);
    }
}
```

#### 6.3.3 Acquire algorithm

All operations run inside an EF Core-managed transaction with `IsolationLevel.ReadCommitted`:

1. `UPDATE OrionGuard_OutboxLocks SET HolderId = @newHolder, AcquiredOnUtc = @now, ExpiresOnUtc = @now + @lease WHERE LockKey = @key AND (HolderId IS NULL OR ExpiresOnUtc <= @now)` — atomic takeover of free or expired locks.
2. If `affectedRows == 0` (no row existed): `INSERT INTO OrionGuard_OutboxLocks (...) VALUES (...)` inside a `try/catch` for unique-key violation; on violation the lock was created by a concurrent acquirer between (1) and (2), return `null`.
3. After commit, `SELECT HolderId FROM OrionGuard_OutboxLocks WHERE LockKey = @key`; if the returned `HolderId == @newHolder`, return a `Handle`; otherwise return `null` (another caller raced us during the gap).

The `@newHolder` is a freshly-allocated `Guid` per acquire call. Per-call ID lets release verify ownership and refuses to release a lock the caller no longer holds (lease expired, another worker took over).

#### 6.3.4 Release

```sql
UPDATE OrionGuard_OutboxLocks
   SET HolderId = NULL, ExpiresOnUtc = @now
 WHERE LockKey = @key AND HolderId = @holderId;
```

Holder-ID guard prevents a slow worker whose lease has expired from clobbering a fresh holder. If the row no longer matches, the release is a no-op.

#### 6.3.5 EF Core integration

`SkipLockedDistributedLock` accepts an `IServiceScopeFactory` and creates a per-acquire scope to resolve `DbContext`. All SQL is executed via `ctx.Database.ExecuteSqlInterpolatedAsync` so provider differences (parameter binding, identifier quoting) stay inside EF Core's translator. The class is provider-agnostic — no `ISqlGenerationHelper` or provider-specific code paths.

### 6.4 Dispatcher integration

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    // existing startup log

    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await using var handle = await _distributedLock.TryAcquireAsync(
                _options.LockKey,
                _options.LockLeaseDuration,
                stoppingToken).ConfigureAwait(false);

            if (handle is null)
            {
                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { break; }
        catch { /* existing per-batch swallow */ }

        try
        {
            await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { break; }
    }
}
```

### 6.5 Options additions

```csharp
public sealed class OutboxOptions
{
    // existing properties...

    public string LockKey { get; set; } = "orion_guard_outbox_dispatcher";

    /// <summary>
    /// Lease duration for the distributed lock. Must exceed the expected wall-clock time of a single
    /// <c>ProcessBatchAsync</c> call. Default 30 seconds.
    /// </summary>
    public TimeSpan LockLeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
}
```

### 6.6 At-least-once semantics preserved

Outbox dispatch was already at-least-once (lost SaveChanges after dispatch → row re-picked). Lease expiry while a worker is mid-batch widens this window slightly: a lagging worker holding an expired lease can still dispatch rows that a fresh worker also picks up. Consumer handlers were already expected to be idempotent; this expectation does not change. Default lease of 30s versus the default batch size of 100 and the default polling interval of 5s puts the lease comfortably outside normal batch wall-clock.

### 6.7 DI wiring — extends the existing `OrionGuardEfCoreOptions` fluent API

The current entry point is `services.AddOrionGuardEfCore<TDbContext>(opts => opts.UseOutbox(...))`. v6.4.0 keeps this surface and threads new registrations through an internal customization list on `OrionGuardEfCoreOptions`:

```csharp
public sealed class OrionGuardEfCoreOptions
{
    // ...existing properties + UseInline / UseOutbox methods

    internal List<Action<IServiceCollection>> ServiceCustomizations { get; } = new();

    public OrionGuardEfCoreOptions UseDistributedLock<TLock>() where TLock : class, IDistributedLock
    {
        ServiceCustomizations.Add(s => s.Replace(ServiceDescriptor.Singleton<IDistributedLock, TLock>()));
        return this;
    }
}
```

`AddOrionGuardEfCore` then performs:

```csharp
if (options.Strategy == DomainEventDispatchStrategy.Outbox)
{
    services.TryAddSingleton<IDistributedLock, SkipLockedDistributedLock>();
    // ...other outbox-strategy registrations (type map, etc.)
}

foreach (var customize in options.ServiceCustomizations)
    customize(services);
```

And the dispatcher factory is updated to resolve the new dependencies from the `IServiceProvider`:

```csharp
services.AddHostedService(sp => new OutboxDispatcherHostedService(
    sp.GetRequiredService<OutboxOptions>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<IDistributedLock>(),
    sp.GetRequiredService<OutboxTypeMapRegistry>(),
    sp.GetRequiredService<OutboxTypeMapOptions>(),
    sp.GetService<ILogger<OutboxDispatcherHostedService>>()));
```

`TryAddSingleton` means consumers who do nothing still get `SkipLockedDistributedLock` automatically. Consumers can replace via `opts.UseDistributedLock<TLock>()` or by registering `IDistributedLock` *before* `AddOrionGuardEfCore`. v6.3 consumers who do not want to apply the new migration call `opts.UseDistributedLock<NullDistributedLock>()` — a shipped no-op handle factory that immediately returns a handle on every call, restoring v6.3 single-instance behaviour.

### 6.8 `NullDistributedLock` — opt-out for unmigrated single-instance consumers

```csharp
public sealed class NullDistributedLock : IDistributedLock
{
    public Task<IDistributedLockHandle?> TryAcquireAsync(string lockKey, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        => Task.FromResult<IDistributedLockHandle?>(new NullHandle(lockKey));

    private sealed class NullHandle(string lockKey) : IDistributedLockHandle
    {
        public string LockKey => lockKey;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
```

Listed in §2 package layout under `Outbox/Locking/`.

## 7. `OutboxTypeMapRegistry` (`OrionGuard.EntityFrameworkCore`)

### 7.1 The current fragility

The dispatcher resolves event types via `Type.GetType(msg.EventType)`, where `msg.EventType` is an assembly-qualified name written at outbox-insert time. This couples persisted payload schema to internal CLR identity: rename `MyApp.Events.UserRegistered` to `MyApp.Domain.Users.UserCreated` and every queued outbox row becomes a dead letter. The dispatcher already carries `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` annotations to reflect this AOT hostility.

### 7.2 Registry

```csharp
namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;

public sealed class OutboxTypeMapRegistry
{
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _byType = new();

    public OutboxTypeMapRegistry Map<TEvent>(string logicalName) where TEvent : IDomainEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        var type = typeof(TEvent);
        if (_byName.TryGetValue(logicalName, out var existing) && existing != type)
            throw new InvalidOperationException($"Outbox type map collision: '{logicalName}' is already mapped to {existing.FullName}.");
        if (_byType.TryGetValue(type, out var existingName) && existingName != logicalName)
            throw new InvalidOperationException($"Outbox type map collision: {type.FullName} is already mapped to '{existingName}'.");
        _byName[logicalName] = type;
        _byType[type] = logicalName;
        return this;
    }

    public bool TryResolve(string logicalName, [NotNullWhen(true)] out Type? type)
        => _byName.TryGetValue(logicalName, out type);

    public bool TryGetLogicalName(Type type, [NotNullWhen(true)] out string? logicalName)
        => _byType.TryGetValue(type, out logicalName);
}

public sealed class OutboxTypeMapOptions
{
    /// <summary>
    /// When true, the dispatcher falls back to <see cref="Type.GetType(string)"/> for event types not in the registry.
    /// Default <see langword="true"/> for backward compatibility with v6.3.x rows.
    /// Set to <see langword="false"/> for AOT deployments.
    /// </summary>
    public bool AllowAssemblyQualifiedNameFallback { get; set; } = true;
}
```

Registration enforces a 1:1 mapping (no silent override; collisions throw at startup). The registry is a singleton populated once at boot — no thread-safety on `Map` because writes only happen synchronously during DI build.

### 7.3 Writer side

At outbox-insert time (in the EF Core `SaveChanges` interceptor that today produces `OutboxMessage` rows for `AggregateRoot.PullDomainEvents`):

```csharp
var eventType = @event.GetType();
var typeId = registry.TryGetLogicalName(eventType, out var name)
    ? name
    : eventType.AssemblyQualifiedName ?? eventType.FullName!;
var msg = new OutboxMessage { EventType = typeId, /* ... */ };
```

When a consumer registers the logical name, all *new* rows are written with the logical name. Older rows already in the table continue to carry their AQN — the reader's fallback handles them.

### 7.4 Reader side

```csharp
Type? type;
if (registry.TryResolve(msg.EventType, out var registered))
{
    type = registered;
}
else if (_typeMapOptions.AllowAssemblyQualifiedNameFallback)
{
    type = Type.GetType(msg.EventType);
}
else
{
    type = null;
}

if (type is null)
{
    msg.Error = $"TYPE_NOT_FOUND: '{msg.EventType}' is not registered in OutboxTypeMapRegistry and AQN fallback is " +
                $"{(_typeMapOptions.AllowAssemblyQualifiedNameFallback ? "enabled but resolution failed" : "disabled")}.";
    msg.ProcessedOnUtc = DateTime.UtcNow;
    _logger?.LogWarning("Outbox row {RowId} dead-lettered: type '{EventType}' could not be resolved.", msg.Id, msg.EventType);
    return; // skip dispatch, persist state
}
```

### 7.5 AOT path

When `AllowAssemblyQualifiedNameFallback = false`, the dispatcher never calls `Type.GetType`. The `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` annotations on `OutboxDispatcherHostedService` remain (because the dispatcher still calls `JsonSerializer.Deserialize(string, Type)` which has its own AOT requirements), but consumers running with full AOT can additionally register a JSON `TypeInfoResolver` and disable AQN fallback to eliminate the `Type.GetType` code path. Local `[UnconditionalSuppressMessage]` on the registry-only branch is acceptable because the consumer has explicitly opted in.

### 7.6 DI helper — extends `OrionGuardEfCoreOptions`

```csharp
public sealed class OrionGuardEfCoreOptions
{
    // ...other Use* methods

    public OrionGuardEfCoreOptions UseOutboxTypeMap(
        Action<OutboxTypeMapRegistry> configure,
        Action<OutboxTypeMapOptions>? configureOptions = null)
    {
        var registry = new OutboxTypeMapRegistry();
        configure(registry);

        var options = new OutboxTypeMapOptions();
        configureOptions?.Invoke(options);

        ServiceCustomizations.Add(s =>
        {
            s.Replace(ServiceDescriptor.Singleton(registry));
            s.Replace(ServiceDescriptor.Singleton(options));
        });
        return this;
    }
}
```

When `AddOrionGuardEfCore` registers Outbox-strategy services it also `TryAddSingleton`s an empty `OutboxTypeMapRegistry` and default `OutboxTypeMapOptions`, so the dispatcher's resolution always succeeds. If consumers never call `UseOutboxTypeMap`, the registry stays empty, options stay at defaults (AQN fallback enabled), and behaviour matches v6.3.x.

## 8. Archival hosted service (`OrionGuard.EntityFrameworkCore`)

### 8.1 The problem

Processed outbox rows (`ProcessedOnUtc != null`) accumulate forever. Production deployments report tables with tens of millions of rows; the polling query `WHERE ProcessedOnUtc IS NULL` slows down because the index still has to step through processed entries.

### 8.2 Service

```csharp
public sealed class OutboxArchivalHostedService : BackgroundService
{
    public async Task<int> ArchiveBatchAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - _options.RetentionPeriod;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DbContext>();

        var query = ctx.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc != null && m.ProcessedOnUtc < cutoff);

        if (_options.PreserveDeadLetters)
            query = query.Where(m => m.Error == null);

        return await query
            .OrderBy(m => m.ProcessedOnUtc)
            .Take(_options.BatchSize)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var handle = await _distributedLock.TryAcquireAsync(
                    _options.LockKey, TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);

                if (handle is null)
                {
                    await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await ArchiveBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger?.LogError(ex, "Outbox archival batch failed."); }

            try { await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
}
```

### 8.3 Options and defaults

```csharp
public sealed class OutboxArchivalOptions
{
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromHours(1);
    public int BatchSize { get; set; } = 1000;
    public bool PreserveDeadLetters { get; set; } = true;
    public string LockKey { get; set; } = "orion_guard_outbox_archival";
}
```

- **30-day retention, 1-hour polling, 1000-row batches** — conservative defaults that keep latency negligible on healthy systems and prevent surprise data loss. Operators tune.
- **`PreserveDeadLetters = true`** — dead-lettered rows (`Error != null && ProcessedOnUtc != null`) are *never* deleted by default. Operators want to inspect failures; silent deletion would defeat the dead-letter pattern. Setting `false` allows full retention-based cleanup.
- **`ExecuteDeleteAsync`** (EF Core 7+) — single SQL DELETE, no entity tracking, batch-safe.
- **Separate lock key** so dispatcher and archival do not block each other.

### 8.4 Opt-in DI helper — extends `OrionGuardEfCoreOptions`

```csharp
public sealed class OrionGuardEfCoreOptions
{
    // ...other Use* methods

    public OrionGuardEfCoreOptions UseOutboxArchival(Action<OutboxArchivalOptions>? configure = null)
    {
        var options = new OutboxArchivalOptions();
        configure?.Invoke(options);

        ServiceCustomizations.Add(s =>
        {
            s.Replace(ServiceDescriptor.Singleton(options));
            s.AddHostedService<OutboxArchivalHostedService>();
        });
        return this;
    }
}
```

Archival is opt-in. Consumers who do not call `UseOutboxArchival` see no behavioural change — no hosted service is registered.

## 9. Source compatibility & migration

### 9.1 v6.3.0 → v6.4.0 contract

- **No breaking API changes.** Existing source compiles unchanged.
- **No required DB migration unless distributed locking is wanted.** Without `OrionGuard_OutboxLocks`, `SkipLockedDistributedLock.TryAcquireAsync` will throw on first call. The dispatcher catches per-batch exceptions and logs; consumers who never deploy the migration but also never scale past one instance are unaffected only if `IDistributedLock` is *not* registered. To keep zero-migration zero-config v6.3.0 behaviour, the outbox builder's default registration must remain `TryAddSingleton<IDistributedLock, SkipLockedDistributedLock>()`, and the dispatcher must tolerate `IDistributedLock` returning `null` *or throwing* — see 9.3.
- **No required `OutboxTypeMapRegistry` registration.** `AddOrionGuardEfCore` (when `Strategy == Outbox`) `TryAddSingleton`s an empty registry and default `OutboxTypeMapOptions`. Resolution falls back to AQN. v6.3.0 rows continue to dispatch.

### 9.2 Migration cheat sheet (CHANGELOG.md draft)

```markdown
### Migration from v6.3.0

- **No breaking source changes.**
- **Distributed locking (recommended for multi-instance deployments):**
  Add an EF Core migration that creates `OrionGuard_OutboxLocks` (script template at `docs/migrations/v6.4.0-outbox-locks.md`).
  No code change needed — when `opts.UseOutbox(...)` is selected, `AddOrionGuardEfCore<TDbContext>(...)` auto-wires `SkipLockedDistributedLock`.
- **Single-instance consumers who do NOT want to apply the new migration:**
  `opts.UseOutbox(...).UseDistributedLock<NullDistributedLock>()` — restores v6.3.x behaviour.
- **Type-safe outbox payloads (optional):**
  `opts.UseOutbox(...).UseOutboxTypeMap(r => r.Map<UserRegistered>("user.registered"));`
  New rows write the logical name; old rows continue to dispatch via AQN fallback.
- **Outbox archival (optional):**
  `opts.UseOutbox(...).UseOutboxArchival(a => a.RetentionPeriod = TimeSpan.FromDays(60));`
- **`BusinessRule` base class (optional):**
  Existing `IBusinessRule` implementations work unchanged. Subclass `BusinessRule` to drop the `MessageKey`/`MessageArgs` boilerplate.
- **`Guard.AgainstBrokenRule` (additive):**
  `Guard.AgainstBrokenRule(new OrderMustHaveItems(order));`
  `Entity.CheckRule` keeps working and now delegates to this internally.
- **`BusinessRuleValidationException` → 422 ProblemDetails (automatic):**
  Wherever `OrionGuardExceptionHandler` is registered, `BusinessRuleValidationException` now returns a `422 Unprocessable Entity` ProblemDetails. Customize via `OrionGuardAspNetCoreOptions.BusinessRuleStatusCode`.
```

### 9.3 Lock-table-missing fault tolerance

`SkipLockedDistributedLock.TryAcquireAsync` wraps its EF Core call in a guard that catches `DbException`/`InvalidOperationException` matching "no such table" / "Invalid object name" and returns `null` on first failure, while logging a one-time warning ("OrionGuard_OutboxLocks table not found; distributed locking is disabled. Apply the v6.4.0 migration to enable."). After the warning, subsequent calls also return `null` until the table is migrated in. This keeps zero-migration upgrades from v6.3.0 from breaking — the dispatcher never enters its batch processing because the lock never acquires — but the operator log makes the situation visible. Single-instance consumers who want v6.3.0 behaviour without migrating call `.UseDistributedLock<NullDistributedLock>()` (a no-op handle factory we ship for this case).

## 10. Branch and PR plan

- **Branch:** `feature/v6.4.0` off `master`.
- **PRs:** Single umbrella PR `feat(v6.4.0): business rule helpers, outbox distributed locking, type map, archival`.
- **Commit shape inside the PR (suggested):**
  1. `feat(core): BusinessRule and AsyncBusinessRule abstract bases`
  2. `feat(core): Guard.AgainstBrokenRule and Entity.CheckRule delegation`
  3. `feat(aspnetcore): ProblemDetails mapping for BusinessRuleValidationException`
  4. `feat(efcore): IDistributedLock abstraction and SkipLockedDistributedLock`
  5. `feat(efcore): OutboxTypeMapRegistry with AQN fallback`
  6. `feat(efcore): OutboxArchivalHostedService`
  7. `chore(release): v6.4.0 version bump and CHANGELOG`
- **Tag:** `v6.4.0` on merge commit to `master`.

## 11. Testing strategy

### 11.1 Core (`tests/Moongazing.OrionGuard.Tests/`)

- `Domain/Rules/BusinessRuleTests.cs` — default `MessageKey == GetType().Name`, abstract `IsBroken`/`DefaultMessage` enforcement, `MessageArgs` override.
- `Domain/Rules/AsyncBusinessRuleTests.cs` — async equivalents including cancellation propagation.
- `Core/Guard_AgainstBrokenRuleTests.cs` — happy path, broken rule throws with rule details, null guard.
- `Core/Guard_AgainstBrokenRuleAsyncTests.cs` — same plus cancellation.
- `Domain/Primitives/EntityCheckRuleDelegationTests.cs` — behavioural equivalence between v6.3 implementation and v6.4 delegation (exception type, message, RuleName, MessageKey).

### 11.2 AspNetCore (`tests/Moongazing.OrionGuard.AspNetCore.Tests/`)

- `ExceptionHandling/OrionGuardExceptionHandler_BusinessRuleTests.cs` — 422 default response, custom `BusinessRuleStatusCode = 400` override, `UseProblemDetails = false` simple-JSON path, log level == Warning.
- `ProblemDetails/OrionGuardProblemDetailsFactory_BusinessRuleTests.cs` — `errors` dictionary keyed by `RuleName`, `Type` URL, `Title`, `Status`.

### 11.3 EntityFrameworkCore (`tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/`)

- `Outbox/Locking/SkipLockedDistributedLockTests.cs` — acquire/release happy path, expired-lease takeover, holder-id mismatch on release becomes no-op, double acquire by the same caller, missing-table fault tolerance (returns null + warning).
- `Outbox/Locking/SkipLockedDistributedLockConcurrencyTests.cs` — integration test using shared SQLite database: spawn 5 tasks calling `TryAcquireAsync`; exactly one returns a handle, the other four return null. Release; another acquires.
- `Outbox/TypeMap/OutboxTypeMapRegistryTests.cs` — `Map` collision throws on name reuse and on type reuse, `TryResolve`/`TryGetLogicalName` roundtrip, empty registry returns false.
- `Outbox/OutboxDispatcher_LockIntegrationTests.cs` — two concurrent `OutboxDispatcherHostedService` instances against the same DbContext share-the-database scenario; total dispatch count equals row count, no duplicates.
- `Outbox/OutboxDispatcher_TypeMapTests.cs` — logical-name path resolves and dispatches, AQN fallback path resolves and dispatches, AQN-fallback-disabled with unregistered name dead-letters with the new error string.
- `Outbox/Archival/OutboxArchivalHostedServiceTests.cs` — retention cutoff respected, batch size respected, dead-letter rows preserved by default, dead-letter rows deleted when `PreserveDeadLetters = false`, lock acquisition required before delete.

### 11.4 Coverage bar

Match v6.3.0 — every new public method has at least one happy-path and one negative-path test; every new public type appears in at least one integration test.

## 12. Documentation deliverables

- `CHANGELOG.md` — `## [6.4.0] - YYYY-MM-DD` section (filled at tag time) using the template in §9.2.
- `docs/migrations/v6.4.0-outbox-locks.md` — EF Core migration template with PG / MSSQL / MySQL / SQLite snippets for `OrionGuard_OutboxLocks`.
- `docs/FEATURES-v6.4.md` — narrative overview matching the existing `FEATURES-v6.x.md` series.
- `src/Moongazing.OrionGuard*/docs/README.md` (per-package READMEs touched only where the package surface changed): core, AspNetCore, EntityFrameworkCore.
- *Optional, post-merge:* publish a docs page at `https://moongazing.dev/orionguard/problems/business-rule-violation` corresponding to the ProblemDetails `Type` URL.

## 13. Out-of-scope confirmations

- No `Moongazing.OrionGuard.Redis` package.
- No `Moongazing.OrionGuard.Distributed` package.
- No push-based outbox notifications.
- No event sourcing primitives.
- No `Guard.Against.X` chain syntax (would be v7.0.0).
- No automatic `BusinessRule` source generator (overkill for the size of the savings).
- No archival → audit table copy-before-delete.
