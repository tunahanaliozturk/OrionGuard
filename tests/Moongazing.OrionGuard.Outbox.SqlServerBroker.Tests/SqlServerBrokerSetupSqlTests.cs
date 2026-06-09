namespace Moongazing.OrionGuard.Outbox.SqlServerBroker.Tests;

public sealed class SqlServerBrokerSetupSqlTests
{
    [Fact]
    public void Create_default_args_emits_all_broker_objects()
    {
        var sql = SqlServerBrokerSetupSql.Create();

        Assert.Contains("CREATE MESSAGE TYPE [OrionGuardOutboxRowInserted]", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE CONTRACT [OrionGuardOutboxContract]", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE QUEUE [OrionGuardOutboxQueue]", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE SERVICE [OrionGuardOutboxService]", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TRIGGER [orionguard_outbox_broker_notify]", sql, StringComparison.Ordinal);
        Assert.Contains("ON [OrionGuard_Outbox]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_escapes_close_bracket_in_table_name()
    {
        var sql = SqlServerBrokerSetupSql.Create(tableName: "Naughty]Outbox");
        Assert.Contains("ON [Naughty]]Outbox]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_escapes_single_quote_in_service_literal()
    {
        var sql = SqlServerBrokerSetupSql.Create(serviceName: "te'st");
        Assert.Contains("TO SERVICE ''te''''st''", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Drop_emits_drop_statements_for_each_broker_object()
    {
        var sql = SqlServerBrokerSetupSql.Drop();

        Assert.Contains("DROP TRIGGER [orionguard_outbox_broker_notify]", sql, StringComparison.Ordinal);
        Assert.Contains("DROP SERVICE [OrionGuardOutboxService]", sql, StringComparison.Ordinal);
        Assert.Contains("DROP QUEUE [OrionGuardOutboxQueue]", sql, StringComparison.Ordinal);
        Assert.Contains("DROP CONTRACT [OrionGuardOutboxContract]", sql, StringComparison.Ordinal);
        Assert.Contains("DROP MESSAGE TYPE [OrionGuardOutboxRowInserted]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_throws_on_null_or_whitespace_table()
    {
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Create(tableName: "  "));
    }

    [Fact]
    public void Create_throws_on_null_or_whitespace_queue()
    {
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Create(queueName: "  "));
    }

    [Fact]
    public void Create_throws_on_null_or_whitespace_service()
    {
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Create(serviceName: "  "));
    }
}
