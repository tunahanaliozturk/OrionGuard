# OrionGuard.Locks.Redis

Redis backend for OrionGuard's outbox `IDistributedLock`.

A bridge package that lets consumers using `OrionGuard.EntityFrameworkCore`'s outbox dispatcher coordinate across replicas through Redis instead of the default DB-backed `SkipLockedDistributedLock`.

## Install

```bash
dotnet add package OrionGuard.Locks.Redis
```

Adds a transitive dependency on `OrionLock.Redis` (>= 0.2.3) and `OrionGuard.EntityFrameworkCore` (>= 6.5.0).

## Use

Connection-string form:

```csharp
services.AddOrionGuardEfCore<AppDbContext>(opts => opts
    .UseOutbox()
    .UseOrionLockRedis("localhost:6379", o => o.KeyPrefix = "myapp:outbox:"));
```

Shared multiplexer form (recommended when other parts of the app already use Redis):

```csharp
services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));

services.AddOrionGuardEfCore<AppDbContext>(opts => opts
    .UseOutbox()
    .UseOrionLockRedis(o => o.KeyPrefix = "myapp:outbox:"));
```

## What it does

Implements OrionGuard's `IDistributedLock` over OrionLock's raw `IDistributedLockProvider` (the single-attempt primitive `Moongazing.OrionLock.Redis.RedisLockProvider`). `TryAcquireAsync` returns `null` immediately on contention; disposing the handle issues an owner-checked release (Lua compare-and-delete on Redis). No blocking-acquire retry, no watchdog renewal, no reentrancy — OrionGuard's outbox dispatcher already polls on its own cadence and tolerates lease loss by design.

## Trade-offs vs the default `SkipLockedDistributedLock`

- **Pros**: removes the `OrionGuard_OutboxLocks` row write/update per polling cycle from the primary database. Useful when the primary DB is hot or read-replicated.
- **Cons**: introduces Redis as a hard dependency for outbox dispatch. If Redis is unreachable, the dispatcher polls but never acquires; outbox messages still safely accumulate (they are written by the same transaction that mutates aggregate state).

## License

MIT.
