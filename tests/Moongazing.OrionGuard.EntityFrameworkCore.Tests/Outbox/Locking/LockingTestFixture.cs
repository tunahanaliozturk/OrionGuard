using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Locking;

public sealed class LockingTestFixture : IAsyncDisposable
{
    public SqliteConnection Connection { get; }
    public IServiceProvider Services { get; }

    public LockingTestFixture()
    {
        Connection = new SqliteConnection("Filename=:memory:");
        Connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<LockingTestDbContext>(o => o.UseSqlite(Connection));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<LockingTestDbContext>());
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<LockingTestDbContext>();
        ctx.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        if (Services is IDisposable d) d.Dispose();
    }
}

public sealed class LockingTestDbContext(DbContextOptions<LockingTestDbContext> options) : DbContext(options)
{
    public DbSet<OutboxLock> Locks => Set<OutboxLock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfiguration(new OutboxLockEntityTypeConfiguration());
}
