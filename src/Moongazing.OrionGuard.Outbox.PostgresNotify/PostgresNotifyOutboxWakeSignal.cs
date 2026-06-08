namespace Moongazing.OrionGuard.Outbox.PostgresNotify;

using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push;
using Npgsql;

/// <summary>
/// Postgres LISTEN/NOTIFY backed <see cref="IOutboxWakeSignal"/>. A long-lived background loop
/// holds a dedicated Npgsql connection and runs <c>LISTEN &lt;channel&gt;;</c> against the
/// configured database. Notifications coalesce into a bounded channel that the dispatcher's
/// <see cref="WaitForNextTickAsync"/> consumes, so newly enqueued outbox rows trigger a
/// dispatch within milliseconds instead of waiting for the polling interval.
/// </summary>
/// <remarks>
/// The polling interval supplied by the dispatcher remains an upper bound on wake latency,
/// so a temporarily unreachable LISTEN connection degrades to polling latency rather than
/// dispatch starvation. <see cref="SignalAsync"/> short-circuits via the same in-memory
/// channel so in-process enqueue paths can still wake the dispatcher when no NOTIFY has
/// fired yet (e.g., the database trigger has not been installed).
/// </remarks>
public sealed partial class PostgresNotifyOutboxWakeSignal :
    BackgroundService,
    IOutboxWakeSignal,
    IAsyncDisposable
{
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(capacity: 1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly IOptions<PostgresNotifyOptions> options;
    private readonly ILogger<PostgresNotifyOutboxWakeSignal> logger;

    /// <summary>Constructor.</summary>
    public PostgresNotifyOutboxWakeSignal(
        IOptions<PostgresNotifyOptions> options,
        ILogger<PostgresNotifyOutboxWakeSignal>? logger = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PostgresNotifyOutboxWakeSignal>.Instance;
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
        var connectionString = options.Value.ConnectionString
            ?? throw new InvalidOperationException(
                "PostgresNotifyOptions.ConnectionString must be set. Bind via " +
                "services.Configure<PostgresNotifyOptions>(...) before adding the hosted service.");
        var channel = options.Value.ChannelName;
        var delay = options.Value.InitialReconnectDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                conn.Notification += OnNotification;
                await conn.OpenAsync(stoppingToken).ConfigureAwait(false);

                await using (var cmd = new NpgsqlCommand($"LISTEN \"{channel}\";", conn))
                {
                    await cmd.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
                }

                LogConnected(logger, channel);
                delay = options.Value.InitialReconnectDelay;

                while (!stoppingToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // listener loop is best-effort and must survive transient failures
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
                delay = next > options.Value.MaxReconnectDelay ? options.Value.MaxReconnectDelay : next;
            }
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        wake.Writer.TryComplete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        wake.Writer.TryComplete();
        await Task.CompletedTask.ConfigureAwait(false);
        base.Dispose();
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs args)
    {
        _ = wake.Writer.TryWrite(true);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "OrionGuard Postgres LISTEN connected on channel '{Channel}'")]
    private static partial void LogConnected(ILogger logger, string channel);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "OrionGuard Postgres LISTEN disconnected; reconnecting in {Delay}")]
    private static partial void LogReconnect(ILogger logger, Exception ex, TimeSpan delay);
}
