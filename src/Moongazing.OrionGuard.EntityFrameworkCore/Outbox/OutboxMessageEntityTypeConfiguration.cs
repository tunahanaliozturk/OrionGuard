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

    /// <summary>Constructs a configuration for the supplied table name (typically from <see cref="OutboxOptions.TableName"/>).</summary>
    public OutboxMessageEntityTypeConfiguration(string tableName) => this.tableName = tableName;

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
        builder.HasIndex(x => new { x.ProcessedOnUtc, x.OccurredOnUtc })
            .HasDatabaseName("IX_OrionGuard_Outbox_Unprocessed");
    }
}
