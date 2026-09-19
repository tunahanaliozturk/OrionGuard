using System.Buffers;

namespace Moongazing.OrionGuard.Outbox.SqlServerBroker;

/// <summary>
/// Reusable T-SQL fragments that set up the Service Broker primitives (message type,
/// contract, queue, service) and the AFTER INSERT trigger that sends a notification on
/// every committed outbox row. Consumers run these against their own connection in a
/// database migration or one-time setup script; this package does NOT auto-install the
/// schema to avoid surprise changes.
/// </summary>
/// <remarks>
/// Every name must be a plain identifier: 1 to 128 ASCII letters, digits or underscores, not starting
/// with a digit. Anything else, including a schema-qualified <c>schema.table</c>, throws
/// <see cref="ArgumentException"/>, because the names are spliced into DDL and into the string that
/// <c>EXEC</c> runs.
/// </remarks>
public static class SqlServerBrokerSetupSql
{
    private const int MaxNameLength = 128;

    private static readonly SearchValues<char> IdentifierChars =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_");

    /// <summary>
    /// Idempotent SQL that creates the Service Broker objects (or returns silently if they
    /// already exist) and binds the AFTER INSERT trigger to the outbox table. Substitute
    /// the optional parameters when the consumer uses a custom outbox <c>TableName</c>.
    /// Service Broker MUST be enabled on the target database; run
    /// <c>ALTER DATABASE [&lt;db&gt;] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;</c> once.
    /// </summary>
    /// <exception cref="ArgumentException">A name is empty or not a plain identifier.</exception>
    public static string Create(
        string tableName = "OrionGuard_Outbox",
        string queueName = "OrionGuardOutboxQueue",
        string serviceName = "OrionGuardOutboxService",
        string contractName = "OrionGuardOutboxContract",
        string messageTypeName = "OrionGuardOutboxRowInserted")
    {
        ValidateName(tableName, nameof(tableName));
        ValidateName(queueName, nameof(queueName));
        ValidateName(serviceName, nameof(serviceName));
        ValidateName(contractName, nameof(contractName));
        ValidateName(messageTypeName, nameof(messageTypeName));

        // The names are plain identifiers by now, so the escapes below change nothing today. They stay as
        // defence in depth: the SQL remains well-formed even if the validation is ever relaxed.
        var tableQ = EscapeIdentifier(tableName);
        var queueQ = EscapeIdentifier(queueName);
        var serviceQ = EscapeIdentifier(serviceName);
        var contractIdQ = EscapeIdentifier(contractName);
        var messageIdQ = EscapeIdentifier(messageTypeName);
        var contractLit = EscapeLiteral(contractName);
        var messageLit = EscapeLiteral(messageTypeName);
        var queueLit = EscapeLiteral(queueName);
        var serviceLit = EscapeLiteral(serviceName);

        // Inside EXEC('...') the trigger body is itself a string literal, so everything spliced into it goes
        // through a second escape for that literal (' -> ''): a bracketed identifier is bracket-escaped for the
        // inner SQL and then quote-doubled, and the SEND TO SERVICE '<name>' literal is quote-doubled twice.
        var tableInExec = EscapeLiteral(tableQ);
        var serviceInExec = EscapeLiteral(serviceQ);
        var contractInExec = EscapeLiteral(contractIdQ);
        var messageInExec = EscapeLiteral(messageIdQ);
        var serviceLiteralForExec = EscapeLiteral(EscapeLiteral(serviceName));

        return $@"
IF NOT EXISTS (SELECT 1 FROM sys.service_message_types WHERE name = N'{messageLit}')
    CREATE MESSAGE TYPE [{messageIdQ}] VALIDATION = NONE;

IF NOT EXISTS (SELECT 1 FROM sys.service_contracts WHERE name = N'{contractLit}')
    CREATE CONTRACT [{contractIdQ}] ([{messageIdQ}] SENT BY INITIATOR);

IF NOT EXISTS (SELECT 1 FROM sys.service_queues WHERE name = N'{queueLit}')
    CREATE QUEUE [{queueQ}];

IF NOT EXISTS (SELECT 1 FROM sys.services WHERE name = N'{serviceLit}')
    CREATE SERVICE [{serviceQ}] ON QUEUE [{queueQ}] ([{contractIdQ}]);

IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'orionguard_outbox_broker_notify')
EXEC ('
CREATE TRIGGER [orionguard_outbox_broker_notify]
ON [{tableInExec}]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @h UNIQUEIDENTIFIER;
    BEGIN DIALOG CONVERSATION @h
        FROM SERVICE [{serviceInExec}]
        TO SERVICE ''{serviceLiteralForExec}''
        ON CONTRACT [{contractInExec}]
        WITH ENCRYPTION = OFF;
    SEND ON CONVERSATION @h
        MESSAGE TYPE [{messageInExec}] (N''row'');
    END CONVERSATION @h;
END;
');
";
    }

    /// <summary>SQL that tears down the trigger and Service Broker objects created by <see cref="Create"/>.</summary>
    /// <exception cref="ArgumentException">A name is empty or not a plain identifier.</exception>
    public static string Drop(
        string tableName = "OrionGuard_Outbox",
        string queueName = "OrionGuardOutboxQueue",
        string serviceName = "OrionGuardOutboxService",
        string contractName = "OrionGuardOutboxContract",
        string messageTypeName = "OrionGuardOutboxRowInserted")
    {
        // tableName is not part of the teardown SQL; it is validated so Create and Drop accept the same names.
        ValidateName(tableName, nameof(tableName));
        ValidateName(queueName, nameof(queueName));
        ValidateName(serviceName, nameof(serviceName));
        ValidateName(contractName, nameof(contractName));
        ValidateName(messageTypeName, nameof(messageTypeName));

        var queueQ = EscapeIdentifier(queueName);
        var serviceQ = EscapeIdentifier(serviceName);
        var contractIdQ = EscapeIdentifier(contractName);
        var messageIdQ = EscapeIdentifier(messageTypeName);

        // SQL Server DML triggers are dropped by name only - DROP TRIGGER <name>; there is
        // NO 'ON <table>' clause for DML triggers (that syntax is DDL-trigger-only).
        return $@"
IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'orionguard_outbox_broker_notify')
    DROP TRIGGER [orionguard_outbox_broker_notify];

IF EXISTS (SELECT 1 FROM sys.services WHERE name = N'{EscapeLiteral(serviceName)}')
    DROP SERVICE [{serviceQ}];

IF EXISTS (SELECT 1 FROM sys.service_queues WHERE name = N'{EscapeLiteral(queueName)}')
    DROP QUEUE [{queueQ}];

IF EXISTS (SELECT 1 FROM sys.service_contracts WHERE name = N'{EscapeLiteral(contractName)}')
    DROP CONTRACT [{contractIdQ}];

IF EXISTS (SELECT 1 FROM sys.service_message_types WHERE name = N'{EscapeLiteral(messageTypeName)}')
    DROP MESSAGE TYPE [{messageIdQ}];
";
    }

    // Allow-list rather than escaping alone: a name ends up both inside brackets and inside the string EXEC
    // runs, and one missed escape layer there once let a name break out. Plain identifiers need neither.
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

    // SQL Server bracketed-identifier escape: double the close bracket.
    private static string EscapeIdentifier(string value) =>
        value.Replace("]", "]]", StringComparison.Ordinal);

    // SQL Server quoted-literal escape: double the single quote.
    private static string EscapeLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
