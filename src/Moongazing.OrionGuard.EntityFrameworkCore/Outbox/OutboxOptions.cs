namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>Configuration for the outbox dispatcher worker.</summary>
public sealed class OutboxOptions
{
    /// <summary>How frequently the worker polls for unprocessed rows. Default 5s.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum rows fetched per polling iteration. Default 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Maximum dispatch attempts before dead-lettering. Default 5.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Outbox database table name. Default <c>OrionGuard_Outbox</c>.</summary>
    public string TableName { get; set; } = "OrionGuard_Outbox";
}
