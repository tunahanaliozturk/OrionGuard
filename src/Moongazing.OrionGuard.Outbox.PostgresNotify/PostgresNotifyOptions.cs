namespace Moongazing.OrionGuard.Outbox.PostgresNotify;

/// <summary>
/// Configuration for <see cref="PostgresNotifyOutboxWakeSignal"/>. The defaults are chosen for
/// a single-tenant outbox table managed by OrionGuard.EntityFrameworkCore; consumers with custom
/// table names override <see cref="ChannelName"/>.
/// </summary>
public sealed class PostgresNotifyOptions
{
    /// <summary>
    /// PostgreSQL connection string used by the long-lived LISTEN connection. Required.
    /// The connection should not be shared with the application's main <c>DbContext</c> pool;
    /// the listener owns its own connection for the lifetime of the dispatcher.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// PostgreSQL NOTIFY channel name. Defaults to <c>orionguard_outbox</c>, matching the
    /// trigger emitted by <see cref="PostgresNotifyTriggerSql"/>. Consumers using a
    /// custom <c>OutboxOptions.TableName</c> override this.
    /// </summary>
    public string ChannelName { get; set; } = "orionguard_outbox";

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
