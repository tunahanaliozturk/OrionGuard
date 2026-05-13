namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>Configuration for the outbox dispatcher worker.</summary>
public sealed class OutboxOptions
{
    private TimeSpan pollingInterval = TimeSpan.FromSeconds(5);
    private int batchSize = 100;
    private int maxRetries = 5;
    private string tableName = "OrionGuard_Outbox";

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
}
