namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Archival;

/// <summary>Configures the opt-in <see cref="OutboxArchivalHostedService"/>.</summary>
public sealed class OutboxArchivalOptions
{
    /// <summary>How long to keep processed rows before deletion. Default 30 days.</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the archival worker polls. Default 1 hour.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Max rows deleted per batch. Default 1000.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// When <see langword="true"/>, rows with <see cref="OutboxMessage.Error"/> set (dead-letter) are
    /// never deleted regardless of retention. Default <see langword="true"/>.
    /// </summary>
    public bool PreserveDeadLetters { get; set; } = true;

    /// <summary>Lock key used to coordinate archival across instances. Default <c>orion_guard_outbox_archival</c>.</summary>
    public string LockKey { get; set; } = "orion_guard_outbox_archival";
}
