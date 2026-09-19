namespace Moongazing.OrionGuard.Outbox.SqlServerBroker.Tests;

public sealed class SqlServerBrokerSetupSqlTests
{
    // Names that are not plain identifiers. The first one closes the EXEC('...') string the trigger body runs
    // in and appends its own statement; the rest cover each rule of the allow-list.
    public static TheoryData<string> InvalidNames => new()
    {
        "Outbox]'); DROP TABLE dbo.Users; --",
        "te'st",
        "Naughty]Outbox",
        "dbo.OrionGuard_Outbox",
        "Outbox\n",
        "1Outbox",
        "Outbox Table",
        "Outböx",
        new string('a', 129),
    };

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
        Assert.Contains("TO SERVICE ''OrionGuardOutboxService''", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_accepts_custom_plain_identifiers()
    {
        var sql = SqlServerBrokerSetupSql.Create(
            tableName: "_App_Outbox2",
            queueName: "AppQueue",
            serviceName: "AppService",
            contractName: "AppContract",
            messageTypeName: new string('m', 128));

        Assert.Contains("ON [_App_Outbox2]", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE QUEUE [AppQueue]", sql, StringComparison.Ordinal);
        Assert.Contains("TO SERVICE ''AppService''", sql, StringComparison.Ordinal);
        Assert.Contains("ON CONTRACT [AppContract]", sql, StringComparison.Ordinal);
        Assert.Contains($"MESSAGE TYPE [{new string('m', 128)}]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_rejects_a_table_name_that_breaks_out_of_the_EXEC_string()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            SqlServerBrokerSetupSql.Create(tableName: "Outbox]'); DROP TABLE dbo.Users; --"));

        Assert.Equal("tableName", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void Create_rejects_a_name_that_is_not_a_plain_identifier(string name)
    {
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Create(tableName: name));
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Create(queueName: name));
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Create(serviceName: name));
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Create(contractName: name));
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Create(messageTypeName: name));
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void Drop_rejects_a_name_that_is_not_a_plain_identifier(string name)
    {
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Drop(tableName: name));
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Drop(queueName: name));
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Drop(serviceName: name));
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Drop(contractName: name));
        Assert.Throws<ArgumentException>(() => SqlServerBrokerSetupSql.Drop(messageTypeName: name));
    }

    [Fact]
    public void Drop_emits_drop_statements_for_each_broker_object()
    {
        var sql = SqlServerBrokerSetupSql.Drop();

        Assert.Contains("DROP TRIGGER [orionguard_outbox_broker_notify]", sql, StringComparison.Ordinal);
        // DML triggers are dropped by name; there must NOT be an 'ON <table>' clause.
        Assert.DoesNotContain("DROP TRIGGER [orionguard_outbox_broker_notify] ON", sql, StringComparison.Ordinal);
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
