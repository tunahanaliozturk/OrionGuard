namespace Moongazing.OrionGuard.Outbox.PostgresNotify.Tests;

public sealed class PostgresNotifyTriggerSqlTests
{
    [Fact]
    public void Create_default_args_emits_pg_notify_on_default_channel()
    {
        var sql = PostgresNotifyTriggerSql.Create();

        Assert.Contains("CREATE OR REPLACE FUNCTION orionguard_outbox_notify_orionguard_outbox", sql, StringComparison.Ordinal);
        Assert.Contains("pg_notify('orionguard_outbox'", sql, StringComparison.Ordinal);
        Assert.Contains("AFTER INSERT ON \"OrionGuard_Outbox\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_with_custom_table_quotes_identifier_and_uses_channel_name()
    {
        var sql = PostgresNotifyTriggerSql.Create(tableName: "App_Outbox", channelName: "app_outbox_v2");

        Assert.Contains("AFTER INSERT ON \"App_Outbox\"", sql, StringComparison.Ordinal);
        Assert.Contains("pg_notify('app_outbox_v2'", sql, StringComparison.Ordinal);
        Assert.Contains("orionguard_outbox_notify_app_outbox_v2()", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_sanitizes_channel_characters_in_function_name()
    {
        // Channel name with mixed case + punctuation must still produce a valid SQL identifier
        // for the function and trigger names.
        var sql = PostgresNotifyTriggerSql.Create(channelName: "Tenant.A-1");

        Assert.Contains("orionguard_outbox_notify_tenant_a_1()", sql, StringComparison.Ordinal);
        Assert.Contains("orionguard_outbox_notify_trigger_tenant_a_1", sql, StringComparison.Ordinal);
        // The pg_notify literal preserves the original channel for client matching.
        Assert.Contains("pg_notify('Tenant.A-1'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Drop_emits_drop_trigger_and_drop_function()
    {
        var sql = PostgresNotifyTriggerSql.Drop();

        Assert.Contains("DROP TRIGGER IF EXISTS orionguard_outbox_notify_trigger_orionguard_outbox ON \"OrionGuard_Outbox\";", sql, StringComparison.Ordinal);
        Assert.Contains("DROP FUNCTION IF EXISTS orionguard_outbox_notify_orionguard_outbox()", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_throws_on_null_or_whitespace_table()
    {
        Assert.Throws<ArgumentException>(() => PostgresNotifyTriggerSql.Create(tableName: "  "));
    }

    [Fact]
    public void Create_throws_on_null_or_whitespace_channel()
    {
        Assert.Throws<ArgumentException>(() => PostgresNotifyTriggerSql.Create(channelName: "  "));
    }
}
