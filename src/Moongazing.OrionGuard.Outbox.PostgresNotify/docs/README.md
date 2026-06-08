# OrionGuard.Outbox.PostgresNotify

Postgres LISTEN/NOTIFY backed `IOutboxWakeSignal` for `OrionGuard.EntityFrameworkCore`. Replaces the v6.5.1 polling-only fallback with an event-driven wake on every committed outbox row when the consumer's database is PostgreSQL.

## Install

```bash
dotnet add package OrionGuard.Outbox.PostgresNotify
```

## Wire-up

```csharp
services.AddPostgresNotifyOutboxWakeSignal(o =>
{
    o.ConnectionString = "Host=localhost;Database=app;Username=app;Password=app";
});

services.AddOrionGuardEfCore<AppDbContext>(opts => opts.UseOutbox());
```

The hosted service opens a dedicated Npgsql connection, runs `LISTEN "orionguard_outbox";`, and signals the dispatcher on every notification. The dispatcher's polling interval upper-bounds wake latency, so an unreachable LISTEN connection degrades to polling rather than dispatch starvation.

## Trigger installation

This package does NOT auto-install the database trigger. Run the SQL once via a migration:

```csharp
public partial class InstallOrionGuardOutboxNotify : Migration
{
    protected override void Up(MigrationBuilder mb) =>
        mb.Sql(PostgresNotifyTriggerSql.Create());
    protected override void Down(MigrationBuilder mb) =>
        mb.Sql(PostgresNotifyTriggerSql.Drop());
}
```

`PostgresNotifyTriggerSql.Create(tableName, channelName)` accepts overrides if you use a custom `OutboxOptions.TableName`.
