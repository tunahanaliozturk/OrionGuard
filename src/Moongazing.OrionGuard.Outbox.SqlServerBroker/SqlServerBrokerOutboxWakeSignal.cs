namespace Moongazing.OrionGuard.Outbox.SqlServerBroker;

using System.Threading.Channels;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;

/// <summary>
/// SQL Server Service Broker backed <see cref="IOutboxWakeSignal"/>. A long-lived background
/// loop holds a dedicated <see cref="SqlConnection"/>, runs
/// <c>WAITFOR (RECEIVE ... FROM &lt;queue&gt;)</c> with a configured timeout, and signals the
/// in-process channel that v6.5.1's <c>ChannelOutboxWakeSignal</c> also uses. Newly enqueued
/// outbox rows that fire the consumer-installed AFTER INSERT trigger trigger a Service
/// Broker conversation; the listener wakes the dispatcher within milliseconds instead of
/// waiting for the polling interval.
/// </summary>
/// <remarks>
/// The dispatcher's polling interval upper-bounds wake latency by contract, so an unreachable
/// SQL Server connection degrades to polling rather than dispatch starvation. The wait is
/// also capped by <see cref="SqlServerBrokerOptions.ReceiveTimeout"/> so the listener loop
/// can re-check the cancellation token periodically without holding a long-running query
/// indefinitely.
/// </remarks>
public sealed partial class SqlServerBrokerOutboxWakeSignal :
    BackgroundService,
    IOutboxWakeSignal
{
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(capacity: 1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly IOptions<SqlServerBrokerOptions> options;
    private readonly ILogger<SqlServerBrokerOutboxWakeSignal> logger;

    /// <summary>Constructor.</summary>
    public SqlServerBrokerOutboxWakeSignal(
        IOptions<SqlServerBrokerOptions> options,
        ILogger<SqlServerBrokerOutboxWakeSignal>? logger = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? NullLogger<SqlServerBrokerOutboxWakeSignal>.Instance;
    }

    /// <inheritdoc />
    public async Task WaitForNextTickAsync(TimeSpan pollingInterval, CancellationToken cancellationToken)
    {
        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pollCts.CancelAfter(pollingInterval);
        try
        {
            await wake.Reader.ReadAsync(pollCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (pollCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Polling interval elapsed without a wake; return normally so the dispatcher
            // runs a polling tick. This is the documented polling-fallback contract.
        }
    }

    /// <inheritdoc />
    public ValueTask SignalAsync(CancellationToken cancellationToken)
    {
        _ = wake.Writer.TryWrite(true);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var connectionString = opts.ConnectionString
            ?? throw new InvalidOperationException(
                "SqlServerBrokerOptions.ConnectionString must be set. Bind via " +
                "services.Configure<SqlServerBrokerOptions>(...) before adding the hosted service.");
        var receiveTimeoutMs = (int)opts.ReceiveTimeout.TotalMilliseconds;
        var delay = opts.InitialReconnectDelay;

        // T-SQL identifier escape: SQL Server quoted identifiers double their close bracket.
        // We splice the queue name into a bracketed identifier so a misconfigured queue name
        // does not malform the RECEIVE statement.
        var quotedQueue = opts.QueueName.Replace("]", "]]", StringComparison.Ordinal);
        var commandText = $@"
WAITFOR (
    RECEIVE TOP(1) conversation_handle, message_type_name
    FROM [{quotedQueue}]
), TIMEOUT {receiveTimeoutMs};
";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync(stoppingToken).ConfigureAwait(false);
                LogConnected(logger, opts.QueueName);
                delay = opts.InitialReconnectDelay;

                while (!stoppingToken.IsCancellationRequested)
                {
                    await using var cmd = new SqlCommand(commandText, conn)
                    {
                        CommandTimeout = receiveTimeoutMs / 1000 + 30,
                    };
                    await using var reader = await cmd.ExecuteReaderAsync(stoppingToken).ConfigureAwait(false);
                    var received = await reader.ReadAsync(stoppingToken).ConfigureAwait(false);
                    if (received)
                    {
                        _ = wake.Writer.TryWrite(true);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // listener loop must survive transient failures
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogReconnect(logger, ex, delay);
                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }

                var next = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                delay = next > opts.MaxReconnectDelay ? opts.MaxReconnectDelay : next;
            }
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        wake.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "OrionGuard SQL Server Service Broker listener connected on queue '{Queue}'")]
    private static partial void LogConnected(ILogger logger, string queue);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "OrionGuard SQL Server Service Broker listener disconnected; reconnecting in {Delay}")]
    private static partial void LogReconnect(ILogger logger, Exception ex, TimeSpan delay);
}
