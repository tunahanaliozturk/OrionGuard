namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>Configuration for the outbox dispatcher worker.</summary>
public sealed class OutboxOptions
{
    private TimeSpan pollingInterval = TimeSpan.FromSeconds(5);
    private int batchSize = 100;
    private int maxRetries = 5;
    private string tableName = "OrionGuard_Outbox";
    private string lockKey = "orion_guard_outbox_dispatcher";
    private TimeSpan lockLeaseDuration = TimeSpan.FromSeconds(30);

    /// <summary>How frequently the worker polls for unprocessed rows. Must be &gt; 0. Default 5s.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a non-positive value.</exception>
    public TimeSpan PollingInterval
    {
        get => pollingInterval;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"{nameof(PollingInterval)} must be greater than zero.");
            }
            pollingInterval = value;
        }
    }

    /// <summary>Maximum rows fetched per polling iteration. Must be &gt; 0. Default 100.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a value &lt;= 0.</exception>
    public int BatchSize
    {
        get => batchSize;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"{nameof(BatchSize)} must be greater than zero.");
            }
            batchSize = value;
        }
    }

    /// <summary>Maximum dispatch attempts before dead-lettering. Must be &gt;= 1. Default 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a value &lt; 1.</exception>
    public int MaxRetries
    {
        get => maxRetries;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"{nameof(MaxRetries)} must be at least 1.");
            }
            maxRetries = value;
        }
    }

    /// <summary>Outbox database table name. Cannot be null or whitespace. Default <c>OrionGuard_Outbox</c>.</summary>
    /// <exception cref="ArgumentException">Thrown when set to <see langword="null"/>, empty, or whitespace.</exception>
    public string TableName
    {
        get => tableName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{nameof(TableName)} cannot be null or whitespace.", nameof(value));
            }
            tableName = value;
        }
    }

    /// <summary>
    /// Lock key used by <see cref="Locking.IDistributedLock"/> to coordinate dispatcher instances.
    /// Cannot be null or whitespace. Default <c>orion_guard_outbox_dispatcher</c>.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when set to null, empty, or whitespace.</exception>
    public string LockKey
    {
        get => lockKey;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{nameof(LockKey)} cannot be null or whitespace.", nameof(value));
            }
            lockKey = value;
        }
    }

    /// <summary>
    /// Lease duration for the distributed lock. Must exceed the wall-clock cost of a single
    /// <see cref="OutboxDispatcherHostedService.ProcessBatchAsync"/> call. Must be &gt; 0. Default 30s.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a non-positive value.</exception>
    public TimeSpan LockLeaseDuration
    {
        get => lockLeaseDuration;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"{nameof(LockLeaseDuration)} must be greater than zero.");
            }
            lockLeaseDuration = value;
        }
    }
}
