# OrionGuard.Outbox.SqlServerBroker

Turns the outbox dispatcher from a poller into a listener on SQL Server: a trigger sends a Service Broker message when rows are inserted, and the dispatcher wakes instead of waiting out its interval.

```bash
dotnet add package OrionGuard.Outbox.SqlServerBroker
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.Outbox.SqlServerBroker;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public static class WakeSignalSetup
{
    public static void Add(IServiceCollection services, string connectionString)
    {
        services.AddSqlServerBrokerOutboxWakeSignal(o => o.ConnectionString = connectionString);
        services.AddOrionGuardEfCore<AppDbContext>(opts => opts.UseOutbox());
    }
}
```

After the one-time setup below, an event committed by one process is dispatched by another within milliseconds instead of up to `PollingInterval`. `OrionGuard.EntityFrameworkCore` and `Microsoft.Data.SqlClient` come along as dependencies.

## One-time setup

Service Broker has to be on for the database. `ALTER DATABASE` cannot run inside a transaction, and `WITH ROLLBACK IMMEDIATE` rolls back other sessions' open transactions — so this is a maintenance-window statement, not a migration step:

```sql
ALTER DATABASE [app] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
```

Skip this statement on Azure SQL Managed Instance: Service Broker is [on by default for every new database there and cannot be turned off](https://learn.microsoft.com/azure/azure-sql/managed-instance/transact-sql-tsql-differences-sql-server#service-broker), and both `ENABLE_BROKER` and `DISABLE_BROKER` are unsupported `ALTER DATABASE` options.

Then create the broker objects and the trigger, for example from a migration:

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

`Create(tableName = "OrionGuard_Outbox", queueName = "OrionGuardOutboxQueue", serviceName = "OrionGuardOutboxService", contractName = "OrionGuardOutboxContract", messageTypeName = "OrionGuardOutboxRowInserted")` creates the message type, contract, queue and service when they are missing, plus an `AFTER INSERT` trigger named `orionguard_outbox_broker_notify` that opens a dialog from the service to itself, sends one message and ends the conversation. `Drop(...)` takes the same parameters and removes everything. For custom names, pass them to both methods and set `SqlServerBrokerOptions.QueueName` to the same queue.

Every name must be a plain identifier: 1 to 128 ASCII letters, digits or underscores, not starting with a digit. Anything else — a schema-qualified `schema.table` included — throws `ArgumentException`, because the names are spliced into DDL and into the string `EXEC` runs, where a stray quote would otherwise break out of the literal.

## How the wake-up works

`AddSqlServerBrokerOutboxWakeSignal` registers `SqlServerBrokerOutboxWakeSignal` as the `IOutboxWakeSignal`, replacing the polling-only default whichever order you call it in, and as a hosted service.

- The hosted service opens a `SqlConnection` of its own — not one from your `DbContext` pool, because the connection sits in `WAITFOR` — and loops on `WAITFOR (RECEIVE TOP(1) conversation_handle FROM [queue]), TIMEOUT <ReceiveTimeout>`, ending each received conversation and waking the dispatcher. A `WAITFOR` that times out with no message wakes nothing.
- Every `SaveChanges`/`SaveChangesAsync` in the same process also signals directly after the save. Wake-ups coalesce: at most one is pending at a time.
- If the connection drops, the listener reconnects with a doubling delay. Until it is back the dispatcher falls back to `OutboxOptions.PollingInterval`, which bounds latency in every case.

## Options

| `SqlServerBrokerOptions` | Default | Notes |
| --- | --- | --- |
| `ConnectionString` | none | Required. |
| `QueueName` | `OrionGuardOutboxQueue` | Must match the queue the setup SQL created. |
| `ServiceName` | `OrionGuardOutboxService` | Used by the setup SQL; the listener does not read it. |
| `ReceiveTimeout` | 30 s | How long one `WAITFOR (RECEIVE ...)` blocks. Must be > 0. |
| `InitialReconnectDelay` | 1 s | First delay after a connection failure; doubles each further failure. Must be > 0. |
| `MaxReconnectDelay` | 30 s | Upper bound on that delay. Must be at least `InitialReconnectDelay`. |

A missing connection string or an invalid value makes the hosted service throw `InvalidOperationException` at host start, so the host does not start.

## What this does not do

- **It is an optimization, not a delivery mechanism.** A wake-up carries no rows: the dispatcher still takes its lock and reads the table, so across replicas only the lock holder dispatches. Correctness rests on the outbox table, which is why `PollingInterval` still bounds the worst case.
- **It needs Service Broker, which Azure SQL Database does not have.** Microsoft's [feature comparison](https://learn.microsoft.com/azure/azure-sql/database/features-comparison) lists Service Broker as **No** for Azure SQL Database and **Yes** for Azure SQL Managed Instance; a full SQL Server has it too. On Azure SQL Database this package cannot be used at all — leave the dispatcher polling, which is a supported configuration and not a fallback hack.
- **One outbox table per database.** The trigger name is fixed and only created when missing, so a second outbox table cannot get its own trigger, and running `Create` again does not update an existing one — drop it first if you change the setup.
- **`AFTER INSERT` only.** A row updated back into the unprocessed state, such as a dashboard replay, fires nothing and waits for the next poll.
- **No schema qualification.** The table is resolved in the default schema of the user that runs the SQL.
- **It raises `Microsoft.Data.SqlClient` for your whole application**, including the one `Microsoft.EntityFrameworkCore.SqlServer` uses. Check that against your other SqlClient consumers before adding it.
- **Shutdown is best effort.** Hosted services stop in reverse registration order, so the listener may stop before the dispatcher; the dispatcher's remaining waits then simply last the polling interval.

## Targets

`net8.0`, `net9.0`, `net10.0`; `Microsoft.Data.SqlClient` 7.x. Use the `OrionGuard.EntityFrameworkCore` build from the same OrionGuard release.

## With the rest of OrionGuard

[OrionGuard.EntityFrameworkCore](https://www.nuget.org/packages/OrionGuard.EntityFrameworkCore) (the outbox) · [OrionGuard.Outbox.PostgresNotify](https://www.nuget.org/packages/OrionGuard.Outbox.PostgresNotify) (the PostgreSQL equivalent) · [OrionGuard.Outbox.Dashboard](https://www.nuget.org/packages/OrionGuard.Outbox.Dashboard) · [OrionGuard.Locks.Redis](https://www.nuget.org/packages/OrionGuard.Locks.Redis)

## Documentation

- [Repository and full documentation](https://github.com/tunahanaliozturk/OrionGuard)
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT. See [LICENSE.txt](https://github.com/tunahanaliozturk/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt).
