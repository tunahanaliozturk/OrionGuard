namespace Moongazing.OrionGuard.Outbox.SqlServerBroker;

/// <summary>
/// Configuration for <see cref="SqlServerBrokerOutboxWakeSignal"/>. Defaults map to the
/// queue and service names emitted by <see cref="SqlServerBrokerSetupSql"/>; consumers using
/// a custom outbox table override <see cref="QueueName"/> and <see cref="ServiceName"/>
/// alongside the SQL helper's overrides.
/// </summary>
public sealed class SqlServerBrokerOptions
{
    /// <summary>
    /// SQL Server connection string used by the long-lived <c>WAITFOR (RECEIVE ...)</c>
    /// connection. Required. The connection should not be shared with the application's main
    /// <c>DbContext</c> pool because the WAITFOR statement blocks for up to the configured
    /// timeout.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Service Broker queue name read by the listener. Defaults to <c>OrionGuardOutboxQueue</c>,
    /// matching the helper SQL. Consumers using a custom outbox table override both this and
    /// the helper SQL's queue name argument.
    /// </summary>
    public string QueueName { get; set; } = "OrionGuardOutboxQueue";

    /// <summary>
    /// Service Broker target service name. Defaults to <c>OrionGuardOutboxService</c>.
    /// </summary>
    public string ServiceName { get; set; } = "OrionGuardOutboxService";

    /// <summary>
    /// Maximum wait the <c>WAITFOR (RECEIVE)</c> call blocks before returning to the listener
    /// loop. Default 30 seconds. The dispatcher's polling interval still upper-bounds the
    /// wake latency even if Service Broker delivers slowly.
    /// </summary>
    public TimeSpan ReceiveTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initial delay before the listener attempts to reconnect after a connection failure.
    /// Successive failures back off up to <see cref="MaxReconnectDelay"/>. Default 1 second.
    /// </summary>
    public TimeSpan InitialReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper bound on the reconnect back-off. Default 30 seconds. While the listener is
    /// disconnected, the dispatcher's polling-interval upper bound still binds, so signal loss
    /// degrades to polling latency rather than dispatch starvation.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);
}
