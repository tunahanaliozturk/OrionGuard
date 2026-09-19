# OrionGuard.Outbox.PostgresNotify

PostgreSQL LISTEN/NOTIFY wake-up for the outbox in `OrionGuard.EntityFrameworkCore`, part of [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). A database trigger sends a notification for every row inserted into the outbox table, and a background listener wakes the dispatcher so new rows are picked up without waiting for the polling interval.

## Install

```bash
dotnet add package OrionGuard.Outbox.PostgresNotify
```

`OrionGuard.EntityFrameworkCore` and `Npgsql` are installed as dependencies.

## Quick start

```csharp
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.Outbox.PostgresNotify;

builder.Services.AddPostgresNotifyOutboxWakeSignal(o =>
    o.ConnectionString = builder.Configuration.GetConnectionString("App"));

builder.Services.AddOrionGuardEfCore<AppDbContext>(opts => opts.UseOutbox());
```

Then install the trigger once, as shown below.

## How it works

`AddPostgresNotifyOutboxWakeSignal` registers `PostgresNotifyOutboxWakeSignal` as the `IOutboxWakeSignal`, replacing the polling-only default whichever order you call it in, and as a hosted service.

- The hosted service opens its own `NpgsqlConnection` (not one from your `DbContext` pool), runs `LISTEN "orionguard_outbox";`, and waits for notifications.
- Each notification wakes the dispatcher. So does every `SaveChanges`/`SaveChangesAsync` in the same process, which signals directly after the save. Wake-ups coalesce: at most one is pending at a time.
- PostgreSQL delivers a notification only when the inserting transaction commits, so rolled-back inserts wake nothing.
- A wake-up does not carry rows. The dispatcher still takes its lock and reads the table, so on several replicas every listener wakes but only the lock holder dispatches.
- If the connection drops, the listener reconnects with a doubling delay (1 s up to 30 s by default). Until then the dispatcher falls back to `OutboxOptions.PollingInterval`, which bounds latency in every case.
- Hosted services stop in reverse registration order, so on shutdown the listener can stop before the dispatcher. The dispatcher's remaining waits then simply last the polling interval until it stops too.

## Trigger installation

The package does not change your schema on its own. Run the helper SQL once, for example from an EF Core migration:

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

- `Create(tableName = "OrionGuard_Outbox", channelName = "orionguard_outbox")` creates (or replaces) the function `orionguard_outbox_notify_<channel>` and the `AFTER INSERT ... FOR EACH ROW` trigger `orionguard_outbox_notify_trigger_<channel>`, which calls `pg_notify(channel, NEW."Id"::text)`. It can be run again safely.
- `Drop(tableName, channelName)` removes both. Pass the same arguments you gave `Create`.
- For a custom outbox table or channel, pass the names to both methods and set `PostgresNotifyOptions.ChannelName` to the same channel.
- The table name is written as a single quoted identifier, so it cannot include a schema prefix; the table must be reachable through the `search_path`.

## Options

| `PostgresNotifyOptions` | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | none | Required. If it is missing, the hosted service throws `InvalidOperationException` and the host does not start. |
| `ChannelName` | `orionguard_outbox` | Must match the channel used by the trigger. |
| `InitialReconnectDelay` | 1 s | First delay after a connection failure; doubles on each further failure. |
| `MaxReconnectDelay` | 30 s | Upper bound for the reconnect delay. |

## Locking on PostgreSQL

The default outbox lock, `SkipLockedDistributedLock`, works on PostgreSQL: it takes the lock table's name, schema and column names from your EF Core model and quotes them, so its SQL reaches the `"OrionGuard_OutboxLocks"` table EF Core creates (map it with `OutboxLockEntityTypeConfiguration` and apply the migration). A single instance can skip the table with `UseDistributedLock<NullDistributedLock>()`; [OrionGuard.Locks.Redis](https://www.nuget.org/packages/OrionGuard.Locks.Redis) is the alternative when Redis is already at hand.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- `Npgsql` 10.0.3 or later. This raises Npgsql for your whole application, so an `Npgsql.EntityFrameworkCore.PostgreSQL` provider must be a release that works with Npgsql 10.
- `OrionGuard.EntityFrameworkCore` of the same version (6.7.0), which uses EF Core 9.0.20 on net8.0/net9.0 and EF Core 10.0.12 on net10.0

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (the outbox), [OrionGuard.Outbox.SqlServerBroker](https://www.nuget.org/packages/OrionGuard.Outbox.SqlServerBroker) (the SQL Server equivalent), [OrionGuard.Locks.Redis](https://www.nuget.org/packages/OrionGuard.Locks.Redis)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
