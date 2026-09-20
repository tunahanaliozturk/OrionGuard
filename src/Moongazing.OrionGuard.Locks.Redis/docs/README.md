# OrionGuard.Locks.Redis

Moves the outbox dispatcher's leader election off your application database and onto Redis, for deployments where every replica polling a lock table is the write you do not want.

```bash
dotnet add package OrionGuard.Locks.Redis
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.Locks.Redis;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public static class OutboxLocking
{
    public static void Add(IServiceCollection services) =>
        services.AddOrionGuardEfCore<AppDbContext>(o => o
            .UseOutbox()
            .UseOrionLockRedis("localhost:6379,abortConnect=false", redis => redis.KeyPrefix = "myapp:outbox:"));
}
```

The dispatcher and the archival worker now take a Redis lease instead of a row in `OrionGuard_OutboxLocks` — that table and its migration are no longer needed. `OrionGuard.EntityFrameworkCore` and `OrionLock.Redis` come along as dependencies.

If your application already owns a multiplexer, hand the bridge the options only:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.Locks.Redis;
using StackExchange.Redis;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public static class SharedMultiplexer
{
    public static void Add(IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false"));

        services.AddOrionGuardEfCore<AppDbContext>(o => o
            .UseOutbox()
            .UseOrionLockRedis(redis => redis.KeyPrefix = "myapp:outbox:"));
    }
}
```

`IConnectionMultiplexer`, `RedisLockOptions` and `IDistributedLockProvider` are all registered with `TryAdd`, so whatever you registered first wins and the matching argument here is ignored — including when you wired Redis through OrionLock's own `AddOrionLock(...).UseRedis(...)`.

## How the lease behaves

`UseOrionLockRedis` replaces the registered `IDistributedLock` with `OrionLockBridgeDistributedLock` over OrionLock's `RedisLockProvider`.

- **Acquire** is a single `SET key token NX PX` with a fresh owner token. On contention it returns `null` immediately and the worker retries on its next poll — there is no blocking wait, no reentrancy.
- **Lease length** is `OutboxOptions.LockLeaseDuration` (default 30 seconds) for the dispatcher and `OutboxArchivalOptions.LockLeaseDuration` (default 5 minutes) for archival.
- **Release** is an owner-checked compare-and-delete in Lua, so a lease another replica has already taken over is left alone. A failed release is swallowed and the key expires by itself.
- **Keys** are `RedisLockOptions.KeyPrefix` (default `orionlock:`) plus the lock key, for example `orionlock:orion_guard_outbox_dispatcher`. `RedisLockOptions.Database` defaults to `-1`, the connection's default database.

## When Redis is not there

**At startup**, the connection-string form calls `ConnectionMultiplexer.Connect` as the hosted services are created. With StackExchange.Redis's default `abortConnect=true`, an unreachable server throws and the host does not start — which is why the examples above pass `abortConnect=false`, so the app starts and the multiplexer reconnects in the background.

**At runtime**, an unreachable Redis makes the acquire call throw; the worker logs it and retries on the next poll. Outbox rows keep accumulating safely, because they are written in the same transaction as your aggregates, and go out once Redis is back.

## What this does not do

- **The lease is never renewed.** A batch that outruns `LockLeaseDuration` can be joined by another replica dispatching the same rows. Delivery is at least once and handlers must be idempotent — that is true of the database lock as well, but a long batch makes it visible here.
- **It does not remove the outbox table.** Only the lock table goes away; `OrionGuard_Outbox` and its migration are still required.
- **It makes Redis a hard dependency of dispatch.** No Redis, no dispatcher and no archival — events queue up in the database instead. Weigh that against the lock-row write you removed.
- **It does not lock anything else.** This is the outbox dispatcher and archival worker only; it is not a general-purpose lock API for your application.
- **No fencing token reaches your handlers.** The bridge hands out a lease, not a monotonically increasing token, so a handler cannot detect that it lost the lock mid-batch.

## Targets

`net8.0`, `net9.0`, `net10.0`; `OrionLock.Redis` 2.x, over StackExchange.Redis. Use the `OrionGuard.EntityFrameworkCore` build that ships with the same OrionGuard release — it targets EF Core 10 on `net10.0` and EF Core 9 on `net8.0`/`net9.0`.

## With the rest of OrionGuard

[OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (the outbox and the `IDistributedLock` contract) · [OrionGuard.Outbox.PostgresNotify](https://www.nuget.org/packages/OrionGuard.Outbox.PostgresNotify) · [OrionGuard.Outbox.SqlServerBroker](https://www.nuget.org/packages/OrionGuard.Outbox.SqlServerBroker) · [OrionLock.Redis](https://www.nuget.org/packages/OrionLock.Redis)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
