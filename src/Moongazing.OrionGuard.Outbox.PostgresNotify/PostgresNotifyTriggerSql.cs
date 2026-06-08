namespace Moongazing.OrionGuard.Outbox.PostgresNotify;

/// <summary>
/// Reusable SQL fragments for installing the PostgreSQL trigger that emits NOTIFY events on
/// every committed insert into the outbox table. Consumers run these against their own
/// connection in a database migration or one-time setup script; this package does not
/// auto-install the trigger to avoid surprise schema changes.
/// </summary>
public static class PostgresNotifyTriggerSql
{
    /// <summary>
    /// SQL that creates (or replaces) the trigger function and binds it to the outbox table.
    /// Substitute <paramref name="tableName"/> with the consumer's actual outbox table
    /// (default <c>OrionGuard_Outbox</c>) and <paramref name="channelName"/> with the
    /// configured NOTIFY channel (default <c>orionguard_outbox</c>).
    /// </summary>
    public static string Create(string tableName = "OrionGuard_Outbox", string channelName = "orionguard_outbox")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);

        // Use a static function name that derives from the channel so two outbox tables in
        // the same database do not collide on the trigger function symbol.
        var funcName = $"orionguard_outbox_notify_{Sanitize(channelName)}";
        var triggerName = $"orionguard_outbox_notify_trigger_{Sanitize(channelName)}";

        // Quoted-identifier escape: PostgreSQL doubles the double quote (`""`). Quoted-literal
        // escape: PostgreSQL doubles the single quote (`''`). Apply both so custom table or
        // channel names containing these characters do not produce malformed SQL or open a
        // splice. The sanitised function and trigger names already contain no quotable
        // characters and are spliced unquoted.
        var tableQuoted = tableName.Replace("\"", "\"\"", StringComparison.Ordinal);
        var channelLiteral = channelName.Replace("'", "''", StringComparison.Ordinal);

        return $@"
CREATE OR REPLACE FUNCTION {funcName}() RETURNS trigger AS $$
BEGIN
    PERFORM pg_notify('{channelLiteral}', NEW.""Id""::text);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS {triggerName} ON ""{tableQuoted}"";
CREATE TRIGGER {triggerName}
AFTER INSERT ON ""{tableQuoted}""
FOR EACH ROW
EXECUTE FUNCTION {funcName}();
";
    }

    /// <summary>
    /// SQL that drops the trigger and function created by <see cref="Create"/>. Useful for
    /// migration rollbacks or environments rolling back to the polling-only behaviour.
    /// </summary>
    public static string Drop(string tableName = "OrionGuard_Outbox", string channelName = "orionguard_outbox")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);

        var funcName = $"orionguard_outbox_notify_{Sanitize(channelName)}";
        var triggerName = $"orionguard_outbox_notify_trigger_{Sanitize(channelName)}";
        var tableQuoted = tableName.Replace("\"", "\"\"", StringComparison.Ordinal);

        return $@"
DROP TRIGGER IF EXISTS {triggerName} ON ""{tableQuoted}"";
DROP FUNCTION IF EXISTS {funcName}();
";
    }

    // Strip everything that is not a-z, 0-9, or _. Keeps the generated identifier safe to
    // splice into the CREATE FUNCTION / TRIGGER statements without quoting.
    private static string Sanitize(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var i = 0;
        foreach (var c in value)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
            {
                buffer[i++] = c;
            }
            else if (c >= 'A' && c <= 'Z')
            {
                buffer[i++] = (char)(c + 32);
            }
            else
            {
                buffer[i++] = '_';
            }
        }
        return new string(buffer[..i]);
    }
}
