# OrionGuard.Outbox.PostgresNotify

Turns the outbox dispatcher from a poller into a listener on PostgreSQL: a trigger fires `NOTIFY` when a row is committed, and the dispatcher wakes instead of waiting out its interval.

```bash
dotnet add package OrionGuard.Outbox.PostgresNotify
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.Outbox.PostgresNotify;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public static class WakeSignalSetup
{
    public static void Add(IServiceCollection services, string connectionString)
    {
        services.AddPostgresNotifyOutboxWakeSignal(o => o.ConnectionString = connectionString);
        services.AddOrionGuardEfCore<AppDbContext>(opts => opts.UseOutbox());
    }
}
```

Install the trigger once (below), and an event committed by one process is dispatched by another within milliseconds instead of up to `PollingInterval`. `OrionGuard.EntityFrameworkCore` and `Npgsql` come along as dependencies.

## Install the trigger

The package does not touch your schema on its own. Run the helper SQL once, from a migration:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;
using Moongazing.OrionGuard.Outbox.PostgresNotify;

public partial class InstallOrionGuardOutboxNotify : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(PostgresNotifyTriggerSql.Create());

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(PostgresNotifyTriggerSql.Drop());
}
```

`Create(tableName = "OrionGuard_Outbox", channelName = "orionguard_outbox")` creates (or replaces) the function `orionguard_outbox_notify_<channel>` and the `AFTER INSERT ... FOR EACH ROW` trigger `orionguard_outbox_notify_trigger_<channel>`, which calls `pg_notify(channel, NEW."Id"::text)`. It is safe to run again. `Drop(tableName, channelName)` removes both — pass the same arguments. For a custom table or channel, pass the names to both methods and set `PostgresNotifyOptions.ChannelName` to the same channel.

Both names must be plain identifiers: 1 to 128 ASCII letters, digits or underscores, not starting with a digit. Anything else throws `ArgumentException`, because the names are spliced into DDL and into the function body — a name with a quote or a `$$` in it could otherwise close the statement it sits in.

## How the wake-up works

`AddPostgresNotifyOutboxWakeSignal` registers `PostgresNotifyOutboxWakeSignal` as the `IOutboxWakeSignal`, replacing the polling-only default whichever order you call it in, and as a hosted service.

- The hosted service opens an `NpgsqlConnection` of its own — not one from your `DbContext` pool, because a `LISTEN` connection is parked indefinitely — runs `LISTEN "orionguard_outbox";` and waits.
- Each notification wakes the dispatcher, as does every `SaveChanges`/`SaveChangesAsync` in the same process. Wake-ups coalesce: at most one is pending at a time.
- PostgreSQL delivers a notification only when the inserting transaction commits, so a rolled-back insert wakes nothing.
- If the connection drops, the listener reconnects with a doubling delay. Until it is back the dispatcher falls back to `OutboxOptions.PollingInterval`, which bounds latency in every case.

## Options

| `PostgresNotifyOptions` | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | none | Required. Missing means the hosted service throws `InvalidOperationException` and the host does not start. |
| `ChannelName` | `orionguard_outbox` | Must match the channel the trigger uses. |
| `InitialReconnectDelay` | 1 s | First delay after a connection failure; doubles each further failure. |
| `MaxReconnectDelay` | 30 s | Upper bound on that delay. |

## Locking on PostgreSQL

This package only wakes the dispatcher; the lock is still the outbox's. The default `SkipLockedDistributedLock` works on PostgreSQL — it takes the lock table's name, schema and columns from your EF Core model and quotes them the way the provider does, which the mixed-case `"OrionGuard_OutboxLocks"` needs. Map it with `OutboxLockEntityTypeConfiguration` and apply the migration. A single instance can skip the table with `UseDistributedLock<NullDistributedLock>()`, and [OrionGuard.Locks.Redis](https://www.nuget.org/packages/OrionGuard.Locks.Redis) is the alternative where Redis is already at hand.

## What this does not do

- **It is an optimization, not a delivery mechanism.** A wake-up carries no rows: the dispatcher still takes its lock and reads the table. On several replicas every listener wakes and only the lock holder dispatches. Delivery correctness rests entirely on the outbox table, which is why `PollingInterval` still bounds the worst case.
- **The trigger is yours to install and keep.** Nothing installs or verifies it at runtime, so a database restored without it silently degrades to polling with no error anywhere.
- **`AFTER INSERT` only.** A row updated back into the unprocessed state — a dashboard replay, say — fires nothing. That row waits for the next poll.
- **No schema prefix.** The table name is written as a single quoted identifier, so `myschema.OrionGuard_Outbox` is rejected; the table must be reachable through the `search_path` of the session that runs the SQL.
- **It raises Npgsql for your whole application.** The Npgsql reference flows to every project that references this package, so your `Npgsql.EntityFrameworkCore.PostgreSQL` provider must be a release that works with that Npgsql major.
- **Shutdown is best effort.** Hosted services stop in reverse registration order, so the listener may stop before the dispatcher; the dispatcher's remaining waits then simply last the polling interval until it stops too.

## Targets

`net8.0`, `net9.0`, `net10.0`; Npgsql 10.x. Use the `OrionGuard.EntityFrameworkCore` build from the same OrionGuard release.

## With the rest of OrionGuard

[OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (the outbox) · [OrionGuard.Outbox.SqlServerBroker](https://www.nuget.org/packages/OrionGuard.Outbox.SqlServerBroker) (the SQL Server equivalent) · [OrionGuard.Outbox.Dashboard](https://www.nuget.org/packages/OrionGuard.Outbox.Dashboard) · [OrionGuard.Locks.Redis](https://www.nuget.org/packages/OrionGuard.Locks.Redis)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
