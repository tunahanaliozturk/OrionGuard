namespace Moongazing.OrionGuard.Outbox.SqlServerBroker;

/// <summary>
/// Reusable T-SQL fragments that set up the Service Broker primitives (message type,
/// contract, queue, service) and the AFTER INSERT trigger that sends a notification on
/// every committed outbox row. Consumers run these against their own connection in a
/// database migration or one-time setup script; this package does NOT auto-install the
/// schema to avoid surprise changes.
/// </summary>
public static class SqlServerBrokerSetupSql
{
    /// <summary>
    /// Idempotent SQL that creates the Service Broker objects (or returns silently if they
    /// already exist) and binds the AFTER INSERT trigger to the outbox table. Substitute
    /// the optional parameters when the consumer uses a custom outbox <c>TableName</c>.
    /// Service Broker MUST be enabled on the target database; run
    /// <c>ALTER DATABASE [&lt;db&gt;] SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;</c> once.
    /// </summary>
    public static string Create(
        string tableName = "OrionGuard_Outbox",
        string queueName = "OrionGuardOutboxQueue",
        string serviceName = "OrionGuardOutboxService",
        string contractName = "OrionGuardOutboxContract",
        string messageTypeName = "OrionGuardOutboxRowInserted")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTypeName);

        var tableQ = EscapeIdentifier(tableName);
        var queueQ = EscapeIdentifier(queueName);
        var serviceQ = EscapeIdentifier(serviceName);
        var contractIdQ = EscapeIdentifier(contractName);
        var messageIdQ = EscapeIdentifier(messageTypeName);
        var contractLit = EscapeLiteral(contractName);
        var messageLit = EscapeLiteral(messageTypeName);
        var queueLit = EscapeLiteral(queueName);
        var serviceLit = EscapeLiteral(serviceName);

        // Inside EXEC('...') the inner trigger body is itself a SQL string literal, so every
        // single quote in the inner SQL doubles for EXEC. For the SEND TO SERVICE '<name>'
        // literal, the name's single quotes go through two layers of escape: the inner
        // literal (' -> '') and the EXEC string (' -> ''), producing four quotes total.
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
ON [{tableQ}]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @h UNIQUEIDENTIFIER;
    BEGIN DIALOG CONVERSATION @h
        FROM SERVICE [{serviceQ}]
        TO SERVICE ''{serviceLiteralForExec}''
        ON CONTRACT [{contractIdQ}]
        WITH ENCRYPTION = OFF;
    SEND ON CONVERSATION @h
        MESSAGE TYPE [{messageIdQ}] (N''row'');
    END CONVERSATION @h;
END;
');
";
    }

    /// <summary>SQL that tears down the trigger and Service Broker objects created by <see cref="Create"/>.</summary>
    public static string Drop(
        string tableName = "OrionGuard_Outbox",
        string queueName = "OrionGuardOutboxQueue",
        string serviceName = "OrionGuardOutboxService",
        string contractName = "OrionGuardOutboxContract",
        string messageTypeName = "OrionGuardOutboxRowInserted")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractName);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageTypeName);

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

    // SQL Server bracketed-identifier escape: double the close bracket.
    private static string EscapeIdentifier(string value) =>
        value.Replace("]", "]]", StringComparison.Ordinal);

    // SQL Server quoted-literal escape: double the single quote.
    private static string EscapeLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);
}
