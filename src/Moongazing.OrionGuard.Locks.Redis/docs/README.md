# OrionGuard.Locks.Redis

Redis-backed `IDistributedLock` for the outbox in `OrionGuard.EntityFrameworkCore`, part of [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). It bridges OrionGuard's lock contract to an [OrionLock](https://github.com/tunahanaliozturk/OrionLock) Redis provider, so outbox replicas coordinate through Redis instead of the `OrionGuard_OutboxLocks` database table.

## Install

```bash
dotnet add package OrionGuard.Locks.Redis
```

`OrionGuard.EntityFrameworkCore` and `OrionLock.Redis` are installed as dependencies.

## Quick start

Connection-string form. The package creates a singleton `IConnectionMultiplexer`:

```csharp
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.Locks.Redis;

builder.Services.AddOrionGuardEfCore<AppDbContext>(o => o
    .UseOutbox()
    .UseOrionLockRedis("localhost:6379,abortConnect=false", redis => redis.KeyPrefix = "myapp:outbox:"));
```

Shared-multiplexer form, for applications that already register one:

```csharp
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.Locks.Redis;
using StackExchange.Redis;

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false"));

builder.Services.AddOrionGuardEfCore<AppDbContext>(o => o
    .UseOutbox()
    .UseOrionLockRedis(redis => redis.KeyPrefix = "myapp:outbox:"));
```

The `OrionGuard_OutboxLocks` table and its migration are not needed with this package.

## How it works

`UseOrionLockRedis` replaces the registered `IDistributedLock` with `OrionLockBridgeDistributedLock`, which sits on OrionLock's `IDistributedLockProvider` (`RedisLockProvider`). It covers both the outbox dispatcher and the archival worker.

- **Acquire.** One `SET key token NX PX` with a new owner token per attempt. On contention it returns `null` at once, and the worker tries again on its next poll. There is no blocking retry, no watchdog renewal, and no reentrancy.
- **Lease.** The lease is `OutboxOptions.LockLeaseDuration` (default 30 s) for the dispatcher and `OutboxArchivalOptions.LockLeaseDuration` (default 5 min) for archival. It is not renewed: if a batch runs longer, another replica can take the lock and dispatch the same rows. Delivery stays at least once, so handlers must be idempotent.
- **Release.** Disposing the handle runs an owner-checked compare-and-delete (Lua), so a lease that another replica has already taken over is left alone. Release errors are swallowed and the lease expires on its own.
- **Keys.** `RedisLockOptions.KeyPrefix` (default `orionlock:`) plus the lock key, for example `orionlock:orion_guard_outbox_dispatcher`. `RedisLockOptions.Database` defaults to -1, the connection's default database.

Registration uses `TryAdd` for `IConnectionMultiplexer`, `RedisLockOptions`, and `IDistributedLockProvider`. If your application already registers any of these (for example through OrionLock's own `AddOrionLock(...).UseRedis(...)`), the existing registration is used and the matching argument here is ignored.

## Failure behavior

- **At startup.** With the connection-string form, `ConnectionMultiplexer.Connect` runs when the hosted services are created. With StackExchange.Redis's default `abortConnect=true`, an unreachable server throws and the host does not start. Add `abortConnect=false`, as above, to start anyway and let the multiplexer reconnect in the background.
- **At runtime.** If Redis is unreachable, the acquire call throws; the worker catches it and tries again on the next poll. Outbox rows keep accumulating in the database, since they are written in the same transaction as your aggregates, and are dispatched once Redis is back.

## Compared with SkipLockedDistributedLock

- Removes the lock-row `UPDATE`/`INSERT` from the application database on every poll.
- Makes Redis a hard dependency for outbox dispatch and archival.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- `OrionLock.Redis` 2.0.0, which brings `OrionLock` 2.0.0 and `StackExchange.Redis` 2.8.16 or later
- `OrionGuard.EntityFrameworkCore` of the same version (6.7.0), which uses EF Core 9.0.20 on net8.0/net9.0 and EF Core 10.0.12 on net10.0

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (outbox and the `IDistributedLock` contract), [OrionLock.Redis](https://www.nuget.org/packages/OrionLock.Redis) (the Redis provider)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
