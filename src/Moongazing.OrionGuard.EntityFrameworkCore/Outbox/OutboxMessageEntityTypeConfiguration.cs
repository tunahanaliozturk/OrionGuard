using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>
/// EF Core fluent configuration for <see cref="OutboxMessage"/>. Apply inside the consumer DbContext's
/// <c>OnModelCreating</c> override (or via <c>ApplyConfiguration</c>) using the configured table name.
/// </summary>
public sealed class OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    private readonly string tableName;
    private readonly string? indexFilter;

    /// <summary>
    /// Creates the configuration for the supplied table name.
    /// </summary>
    /// <param name="tableName">The outbox table name (typically <see cref="OutboxOptions.TableName"/>).</param>
    /// <param name="indexFilter">
    /// Optional provider-specific filter SQL for the unprocessed-rows index. Strongly recommended in
    /// production to avoid an ever-growing index of dead-lettered/processed rows.
    /// <list type="bullet">
    ///   <item><description>SQL Server / PostgreSQL: <c>"[ProcessedOnUtc] IS NULL"</c></description></item>
    ///   <item><description>SQLite: <c>"\"ProcessedOnUtc\" IS NULL"</c></description></item>
    ///   <item><description>If <see langword="null"/>, a non-filtered composite index on
    ///   <c>(ProcessedOnUtc, OccurredOnUtc)</c> is created so the worker's
    ///   <c>WHERE ProcessedOnUtc IS NULL ORDER BY OccurredOnUtc</c> query stays index-covered.</description></item>
    /// </list>
    /// </param>
    public OutboxMessageEntityTypeConfiguration(string tableName, string? indexFilter = null)
    {
        this.tableName = tableName;
        this.indexFilter = indexFilter;
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(tableName);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.TraceParent).HasMaxLength(64);
        builder.Property(x => x.TraceState).HasMaxLength(256);

        if (indexFilter is not null)
        {
            builder.HasIndex(x => x.OccurredOnUtc)
                .HasDatabaseName("IX_OrionGuard_Outbox_Unprocessed")
                .HasFilter(indexFilter);
        }
        else
        {
            builder.HasIndex(x => new { x.ProcessedOnUtc, x.OccurredOnUtc })
                .HasDatabaseName("IX_OrionGuard_Outbox_Unprocessed");
        }
    }
}
