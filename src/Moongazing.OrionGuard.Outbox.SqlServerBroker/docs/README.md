# OrionGuard.Outbox.SqlServerBroker

SQL Server Service Broker wake-up for the outbox in `OrionGuard.EntityFrameworkCore`, part of [OrionGuard](https://github.com/tunahanaliozturk/OrionGuard). A trigger on the outbox table sends a Service Broker message whenever rows are inserted, and a background listener wakes the dispatcher so new rows are picked up without waiting for the polling interval.

## Install

```bash
dotnet add package OrionGuard.Outbox.SqlServerBroker
```

`OrionGuard.EntityFrameworkCore` and `Microsoft.Data.SqlClient` are installed as dependencies.

## Quick start

```csharp
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.Outbox.SqlServerBroker;

builder.Services.AddSqlServerBrokerOutboxWakeSignal(o =>
    o.ConnectionString = builder.Configuration.GetConnectionString("App"));

builder.Services.AddOrionGuardEfCore<AppDbContext>(opts => opts.UseOutbox());
```

Then run the one-time setup below.

## How it works

`AddSqlServerBrokerOutboxWakeSignal` registers `SqlServerBrokerOutboxWakeSignal` as the `IOutboxWakeSignal`, replacing the polling-only default whichever order you call it in, and as a hosted service.

- The hosted service opens its own `SqlConnection` (not one from your `DbContext` pool) and loops on `WAITFOR (RECEIVE TOP(1) conversation_handle FROM [queue]), TIMEOUT <ReceiveTimeout>`. It ends each received conversation and wakes the dispatcher. A `WAITFOR` that times out without a message wakes nothing.
- Every `SaveChanges`/`SaveChangesAsync` in the same process also signals directly after the save. Wake-ups coalesce: at most one is pending at a time.
- A wake-up does not carry rows. The dispatcher still takes its lock and reads the table, so on several replicas only the lock holder dispatches.
- If the connection drops, the listener reconnects with a doubling delay (1 s up to 30 s by default). Until then the dispatcher falls back to `OutboxOptions.PollingInterval`, which bounds latency in every case.
- Hosted services stop in reverse registration order, so on shutdown the listener can stop before the dispatcher. The dispatcher's remaining waits then simply last the polling interval until it stops too.

## One-time setup

Enable Service Broker on the database. `ALTER DATABASE` cannot run inside a transaction, and `WITH ROLLBACK IMMEDIATE` rolls back other sessions' open transactions:

```sql
ALTER DATABASE [app] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
```

Then create the Service Broker objects and the trigger, for example from an EF Core migration:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;
using Moongazing.OrionGuard.Outbox.SqlServerBroker;

public partial class InstallOrionGuardOutboxBroker : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(SqlServerBrokerSetupSql.Create());

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(SqlServerBrokerSetupSql.Drop());
}
```

- `Create(tableName = "OrionGuard_Outbox", queueName = "OrionGuardOutboxQueue", serviceName = "OrionGuardOutboxService", contractName = "OrionGuardOutboxContract", messageTypeName = "OrionGuardOutboxRowInserted")` creates the message type, contract, queue, and service if they do not exist, plus an `AFTER INSERT` trigger named `orionguard_outbox_broker_notify`. The trigger opens a dialog from the service to itself, sends one message, and ends the conversation.
- `Drop(...)` takes the same parameters and removes the trigger and the Service Broker objects.
- For custom names, pass them to both methods and set `SqlServerBrokerOptions.QueueName` to the same queue.
- Every name must be a plain identifier: 1 to 128 ASCII letters, digits or underscores, not starting with a digit. Anything else, including a schema-qualified `schema.table`, throws `ArgumentException`, because the names are spliced into DDL and into the string that `EXEC` runs. The table is resolved in the default schema of the user that runs the SQL.
- The trigger name is fixed and only created when missing, so the helper supports one outbox table per database, and running `Create` again does not update an existing trigger.

## Options

| `SqlServerBrokerOptions` | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | none | Required. |
| `QueueName` | `OrionGuardOutboxQueue` | Must match the queue created by the setup SQL. |
| `ServiceName` | `OrionGuardOutboxService` | Not read by the listener. |
| `ReceiveTimeout` | 30 s | How long one `WAITFOR (RECEIVE ...)` blocks. Must be > 0. |
| `InitialReconnectDelay` | 1 s | First delay after a connection failure; doubles on each further failure. Must be > 0. |
| `MaxReconnectDelay` | 30 s | Upper bound for the reconnect delay. Must be at least `InitialReconnectDelay`. |

A missing connection string or an invalid value makes the hosted service throw `InvalidOperationException` when the host starts, so the host does not start.

## Targets

- `net8.0`, `net9.0`, `net10.0`
- `Microsoft.Data.SqlClient` 7.1.0 or later. This raises SqlClient for your whole application, including the one used by `Microsoft.EntityFrameworkCore.SqlServer`.
- `OrionGuard.EntityFrameworkCore` of the same version (6.7.0), which uses EF Core 9.0.20 on net8.0/net9.0 and EF Core 10.0.12 on net10.0

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- Related packages: [OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (the outbox), [OrionGuard.Outbox.PostgresNotify](https://www.nuget.org/packages/OrionGuard.Outbox.PostgresNotify) (the PostgreSQL equivalent)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
