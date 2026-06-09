# OrionGuard.Outbox.SqlServerBroker

SQL Server Service Broker backed `IOutboxWakeSignal` for `OrionGuard.EntityFrameworkCore`. Sibling to `OrionGuard.Outbox.PostgresNotify` (v6.5.2) for the SQL Server provider.

## Install

```bash
dotnet add package OrionGuard.Outbox.SqlServerBroker
```

## Wire-up

```csharp
services.AddSqlServerBrokerOutboxWakeSignal(o =>
{
    o.ConnectionString = "Server=db;Database=app;User Id=app;Password=app;TrustServerCertificate=true";
});

services.AddOrionGuardEfCore<AppDbContext>(opts => opts.UseOutbox());
```

The hosted service opens a dedicated `SqlConnection`, runs `WAITFOR (RECEIVE ... FROM <queue>)` with a bounded timeout, and signals the dispatcher when a row arrives. The dispatcher's polling interval continues to upper-bound wake latency.

## One-time setup

Service Broker must be enabled on the target database:

```sql
ALTER DATABASE [app] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
```

Then run the helper SQL once via an EF Core migration:

```csharp
public partial class InstallOrionGuardOutboxBroker : Migration
{
    protected override void Up(MigrationBuilder mb) =>
        mb.Sql(SqlServerBrokerSetupSql.Create());
    protected override void Down(MigrationBuilder mb) =>
        mb.Sql(SqlServerBrokerSetupSql.Drop());
}
```

`SqlServerBrokerSetupSql.Create` accepts overrides for custom table / queue / service / contract / message-type names.
