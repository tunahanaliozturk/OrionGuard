namespace Moongazing.OrionGuard.Outbox.PostgresNotify.Tests;

public sealed class PostgresNotifyTriggerSqlTests
{
    // Names that are not plain identifiers. The first two end the function body or the quoted table name
    // and append their own statements; the rest cover each rule of the allow-list.
    public static TheoryData<string> InvalidNames => new()
    {
        "x', NEW.\"Id\"::text); RETURN NEW; END; $$ LANGUAGE plpgsql; DROP TABLE users; --",
        "Outbox\"; DROP TABLE users; --",
        "a$$b",
        "te'st",
        "Tenant.A-1",
        "public.OrionGuard_Outbox",
        "Outbox\n",
        "1Outbox",
        "Outböx",
        new string('a', 129),
    };

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
    public void Create_lowers_the_channel_in_function_and_trigger_names_but_not_in_pg_notify()
    {
        var sql = PostgresNotifyTriggerSql.Create(channelName: "Tenant_A1");

        Assert.Contains("orionguard_outbox_notify_tenant_a1()", sql, StringComparison.Ordinal);
        Assert.Contains("orionguard_outbox_notify_trigger_tenant_a1", sql, StringComparison.Ordinal);
        // The pg_notify literal preserves the original channel for client matching.
        Assert.Contains("pg_notify('Tenant_A1'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_quotes_the_function_body_with_a_named_dollar_tag()
    {
        var sql = PostgresNotifyTriggerSql.Create();

        // A bare $$ body ends at the first $$ inside it.
        Assert.DoesNotContain("$$", sql, StringComparison.Ordinal);
        Assert.Contains("RETURNS trigger AS $orionguard_outbox_notify$", sql, StringComparison.Ordinal);
        Assert.Contains("$orionguard_outbox_notify$ LANGUAGE plpgsql;", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_rejects_a_channel_that_ends_the_function_body()
    {
        var exception = Assert.Throws<ArgumentException>(() => PostgresNotifyTriggerSql.Create(
            channelName: "x', NEW.\"Id\"::text); RETURN NEW; END; $$ LANGUAGE plpgsql; DROP TABLE users; --"));

        Assert.Equal("channelName", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void Create_rejects_a_name_that_is_not_a_plain_identifier(string name)
    {
        Assert.Throws<ArgumentException>(() => PostgresNotifyTriggerSql.Create(tableName: name));
        Assert.Throws<ArgumentException>(() => PostgresNotifyTriggerSql.Create(channelName: name));
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void Drop_rejects_a_name_that_is_not_a_plain_identifier(string name)
    {
        Assert.Throws<ArgumentException>(() => PostgresNotifyTriggerSql.Drop(tableName: name));
        Assert.Throws<ArgumentException>(() => PostgresNotifyTriggerSql.Drop(channelName: name));
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
