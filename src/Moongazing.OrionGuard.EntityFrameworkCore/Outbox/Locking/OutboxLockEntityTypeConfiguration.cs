using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

/// <summary>
/// EF Core mapping for <see cref="OutboxLock"/>. Apply inside your <c>OnModelCreating</c>:
/// <c>modelBuilder.ApplyConfiguration(new OutboxLockEntityTypeConfiguration());</c>.
/// </summary>
public sealed class OutboxLockEntityTypeConfiguration : IEntityTypeConfiguration<OutboxLock>
{
    public void Configure(EntityTypeBuilder<OutboxLock> builder)
    {
        builder.ToTable("OrionGuard_OutboxLocks");
        builder.HasKey(x => x.LockKey);
        builder.Property(x => x.LockKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.HolderId);
        builder.Property(x => x.AcquiredOnUtc);
        builder.Property(x => x.ExpiresOnUtc);
    }
}
