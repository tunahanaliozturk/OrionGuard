using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Locking;

/// <summary>
/// The lock's raw SQL per provider. PostgreSQL folds unquoted identifiers to lower case, so they must be
/// delimited to reach the "OrionGuard_OutboxLocks" table EF Core creates. No database is contacted.
/// </summary>
public class SkipLockedDistributedLockSqlTests
{
    [Fact]
    public void Statements_Npgsql_DelimitEveryIdentifierWithDoubleQuotes()
    {
        using var db = new LockSqlDbContext(o => o.UseNpgsql("Host=unused"));

        var sql = SkipLockedDistributedLock.LockStatements.For(db)!;

        Assert.Equal(
            "UPDATE \"OrionGuard_OutboxLocks\" SET \"HolderId\" = {0}, \"AcquiredOnUtc\" = {1}, \"ExpiresOnUtc\" = {2} " +
            "WHERE \"LockKey\" = {3} AND (\"HolderId\" IS NULL OR \"ExpiresOnUtc\" <= {1})",
            sql.Acquire);
        Assert.Equal(
            "INSERT INTO \"OrionGuard_OutboxLocks\" (\"LockKey\", \"HolderId\", \"AcquiredOnUtc\", \"ExpiresOnUtc\") SELECT {0}, {1}, {2}, {3} " +
            "WHERE NOT EXISTS (SELECT 1 FROM \"OrionGuard_OutboxLocks\" WHERE \"LockKey\" = {0})",
            sql.Insert);
        Assert.Equal(
            "UPDATE \"OrionGuard_OutboxLocks\" SET \"HolderId\" = NULL, \"ExpiresOnUtc\" = {0} WHERE \"LockKey\" = {1} AND \"HolderId\" = {2}",
            sql.Release);
        Assert.Equal("OrionGuard_OutboxLocks", sql.TableName);
    }

    [Fact]
    public void Statements_Npgsql_WithASchema_QualifyTheTable()
    {
        using var db = new LockSqlDbContext(o => o.UseNpgsql("Host=unused"), schema: "ops");

        var sql = SkipLockedDistributedLock.LockStatements.For(db)!;

        // Npgsql quotes only identifiers that need it: an all-lower-case name means the same either way.
        Assert.StartsWith("UPDATE ops.\"OrionGuard_OutboxLocks\" SET", sql.Acquire);
        Assert.Contains("SELECT 1 FROM ops.\"OrionGuard_OutboxLocks\"", sql.Insert);
    }

    [Fact]
    public void Statements_SqlServer_UseBrackets()
    {
        using var db = new LockSqlDbContext(o => o.UseSqlServer("Server=unused"));

        var sql = SkipLockedDistributedLock.LockStatements.For(db)!;

        Assert.Equal(
            "UPDATE [OrionGuard_OutboxLocks] SET [HolderId] = {0}, [AcquiredOnUtc] = {1}, [ExpiresOnUtc] = {2} " +
            "WHERE [LockKey] = {3} AND ([HolderId] IS NULL OR [ExpiresOnUtc] <= {1})",
            sql.Acquire);
        Assert.Equal(
            "UPDATE [OrionGuard_OutboxLocks] SET [HolderId] = NULL, [ExpiresOnUtc] = {0} WHERE [LockKey] = {1} AND [HolderId] = {2}",
            sql.Release);
    }

    [Fact]
    public void Statements_Sqlite_UseDoubleQuotes()
    {
        using var db = new LockSqlDbContext(o => o.UseSqlite("Data Source=:memory:"));

        var sql = SkipLockedDistributedLock.LockStatements.For(db)!;

        Assert.StartsWith("UPDATE \"OrionGuard_OutboxLocks\" SET \"HolderId\" = {0}", sql.Acquire);
    }

    [Fact]
    public void Statements_FollowTheColumnNamesTheModelMaps()
    {
        // e.g. a snake_case naming convention; Npgsql leaves all-lower-case identifiers unquoted.
        using var db = new LockSqlDbContext(o => o.UseNpgsql("Host=unused"), renameColumns: true);

        var sql = SkipLockedDistributedLock.LockStatements.For(db)!;

        Assert.Equal(
            "UPDATE outbox_locks SET holder_id = {0}, acquired_on_utc = {1}, expires_on_utc = {2} " +
            "WHERE lock_key = {3} AND (holder_id IS NULL OR expires_on_utc <= {1})",
            sql.Acquire);
    }

    [Fact]
    public void Statements_OutboxLockNotMapped_ReturnsNull()
    {
        using var db = new UnmappedDbContext();

        Assert.Null(SkipLockedDistributedLock.LockStatements.For(db));
    }

    private sealed class LockSqlDbContext : DbContext
    {
        private readonly Action<DbContextOptionsBuilder> useProvider;
        private readonly string? schema;
        private readonly bool renameColumns;

        public LockSqlDbContext(Action<DbContextOptionsBuilder> useProvider, string? schema = null, bool renameColumns = false)
        {
            this.useProvider = useProvider;
            this.schema = schema;
            this.renameColumns = renameColumns;
        }

        // EF Core caches one model per context type; the schema and column variants each need their own.
        public (string? Schema, bool RenameColumns) Shape => (schema, renameColumns);

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            useProvider(optionsBuilder);
            optionsBuilder.ReplaceService<IModelCacheKeyFactory, ShapeModelCacheKeyFactory>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (schema is not null)
            {
                modelBuilder.HasDefaultSchema(schema);
            }
            modelBuilder.Entity<OutboxLock>(b =>
            {
                new OutboxLockEntityTypeConfiguration().Configure(b);
                if (renameColumns)
                {
                    b.ToTable("outbox_locks");
                    b.Property(x => x.LockKey).HasColumnName("lock_key");
                    b.Property(x => x.HolderId).HasColumnName("holder_id");
                    b.Property(x => x.AcquiredOnUtc).HasColumnName("acquired_on_utc");
                    b.Property(x => x.ExpiresOnUtc).HasColumnName("expires_on_utc");
                }
            });
        }
    }

    private sealed class ShapeModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is LockSqlDbContext shaped ? (object)(context.GetType(), shaped.Shape, designTime) : (context.GetType(), designTime);
    }

    private sealed class UnmappedDbContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite("Data Source=:memory:");
    }
}
