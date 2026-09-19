using System.Buffers;

namespace Moongazing.OrionGuard.Outbox.PostgresNotify;

/// <summary>
/// Reusable SQL fragments for installing the PostgreSQL trigger that emits NOTIFY events on
/// every committed insert into the outbox table. Consumers run these against their own
/// connection in a database migration or one-time setup script; this package does not
/// auto-install the trigger to avoid surprise schema changes.
/// </summary>
/// <remarks>
/// Both names must be plain identifiers: 1 to 128 ASCII letters, digits or underscores, not starting
/// with a digit. Anything else, including a schema-qualified <c>schema.table</c>, throws
/// <see cref="ArgumentException"/>, because the names are spliced into DDL and into the function body.
/// </remarks>
public static class PostgresNotifyTriggerSql
{
    private const int MaxNameLength = 128;

    // Dollar-quote tag of the function body. A body quoted with a bare $$ ends at the first $$ inside it;
    // a named tag can only be ended by itself, and a validated name cannot contain '$'.
    private const string BodyTag = "$orionguard_outbox_notify$";

    private static readonly SearchValues<char> IdentifierChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_");

    /// <summary>
    /// SQL that creates (or replaces) the trigger function and binds it to the outbox table.
    /// Substitute <paramref name="tableName"/> with the consumer's actual outbox table
    /// (default <c>OrionGuard_Outbox</c>) and <paramref name="channelName"/> with the
    /// configured NOTIFY channel (default <c>orionguard_outbox</c>).
    /// </summary>
    /// <exception cref="ArgumentException">A name is empty or not a plain identifier.</exception>
    public static string Create(string tableName = "OrionGuard_Outbox", string channelName = "orionguard_outbox")
    {
        ValidateName(tableName, nameof(tableName));
        ValidateName(channelName, nameof(channelName));

        // Function and trigger names derive from the channel so two outbox tables in the same database do not
        // collide. They are spliced unquoted, which folds them to lower case, so they are lowered here to keep
        // the emitted SQL readable and identical to what PostgreSQL stores.
        var funcName = $"orionguard_outbox_notify_{channelName.ToLowerInvariant()}";
        var triggerName = $"orionguard_outbox_notify_trigger_{channelName.ToLowerInvariant()}";

        // The names are plain identifiers by now, so the escapes below change nothing today. They stay as
        // defence in depth: quoted identifiers double '"', string literals double '\''.
        var tableQuoted = tableName.Replace("\"", "\"\"", StringComparison.Ordinal);
        var channelLiteral = channelName.Replace("'", "''", StringComparison.Ordinal);

        return $@"
CREATE OR REPLACE FUNCTION {funcName}() RETURNS trigger AS {BodyTag}
BEGIN
    PERFORM pg_notify('{channelLiteral}', NEW.""Id""::text);
    RETURN NEW;
END;
{BodyTag} LANGUAGE plpgsql;

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
    /// <exception cref="ArgumentException">A name is empty or not a plain identifier.</exception>
    public static string Drop(string tableName = "OrionGuard_Outbox", string channelName = "orionguard_outbox")
    {
        ValidateName(tableName, nameof(tableName));
        ValidateName(channelName, nameof(channelName));

        var funcName = $"orionguard_outbox_notify_{channelName.ToLowerInvariant()}";
        var triggerName = $"orionguard_outbox_notify_trigger_{channelName.ToLowerInvariant()}";
        var tableQuoted = tableName.Replace("\"", "\"\"", StringComparison.Ordinal);

        return $@"
DROP TRIGGER IF EXISTS {triggerName} ON ""{tableQuoted}"";
DROP FUNCTION IF EXISTS {funcName}();
";
    }

    // Allow-list rather than escaping alone: the channel lands inside a dollar-quoted function body, where
    // quote escaping does not help, and a '$$' in it once ended the body early. Plain identifiers need no
    // escaping anywhere.
    private static void ValidateName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaxNameLength || char.IsAsciiDigit(value[0]) || value.AsSpan().ContainsAnyExcept(IdentifierChars))
        {
            throw new ArgumentException(
                $"{parameterName} must be a plain SQL identifier: 1 to {MaxNameLength} ASCII letters, digits or underscores, not starting with a digit.",
                parameterName);
        }
    }
}
