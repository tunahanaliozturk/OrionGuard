using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

/// <summary>
/// The wiring a consumer uses (<c>AddDbContext((provider, o) =&gt; ...UseOrionGuardDomainEvents(provider))</c> plus
/// <c>AddOrionGuardEfCore</c>) over one shared in-memory SQLite connection, so the worker's own scopes see the
/// same database. Scope validation is on, as in Development.
/// </summary>
public static class OutboxTestServices
{
    public static ServiceProvider Build(
        Func<IServiceProvider, IDomainEventDispatcher> dispatcher,
        bool outbox = true,
        Action<OutboxOptions>? configureOutbox = null)
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        services.AddSingleton(connection);
        services.AddDbContext<TestDbContext>((provider, o) =>
            o.UseSqlite(provider.GetRequiredService<SqliteConnection>()).UseOrionGuardDomainEvents(provider));
        services.AddScoped(dispatcher);
        services.AddOrionGuardEfCore<TestDbContext>(o =>
        {
            if (outbox)
            {
                o.UseOutbox(x =>
                {
                    x.PollingInterval = TimeSpan.FromMilliseconds(50);
                    x.MaxRetries = 3;
                    configureOutbox?.Invoke(x);
                });
            }
            else
            {
                o.UseInline();
            }
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreated();
        return provider;
    }

    public static OutboxMessage Row(IDomainEvent @event, DateTime? occurredOnUtc = null) => new()
    {
        EventType = @event.GetType().AssemblyQualifiedName!,
        Payload = JsonSerializer.Serialize(@event, @event.GetType()),
        OccurredOnUtc = occurredOnUtc ?? @event.OccurredOnUtc,
    };

    public static async Task SeedAsync(IServiceProvider services, params object[] entities)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    public static async Task<List<OutboxMessage>> RowsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages
            .AsNoTracking().OrderBy(m => m.OccurredOnUtc).ToListAsync();
    }

    public static async Task<int> OrderCountAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Orders.CountAsync();
    }

    public static OutboxDispatcherHostedService Worker(
        IServiceProvider services, ILogger<OutboxDispatcherHostedService>? logger = null)
        => new(
            services.GetRequiredService<OutboxOptions>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            new NullDistributedLock(),
            logger: logger);

    /// <summary>Polls until <paramref name="condition"/> holds or <paramref name="timeout"/> passes.</summary>
    public static async Task<bool> EventuallyAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }
            await Task.Delay(20);
        }
        return await condition();
    }
}

/// <summary>Records every dispatched event; optionally runs a callback first.</summary>
public sealed class RecordingDispatcher : IDomainEventDispatcher
{
    private readonly List<IDomainEvent> dispatched = new();
    private readonly Func<IDomainEvent, CancellationToken, Task>? before;

    public RecordingDispatcher(Func<IDomainEvent, CancellationToken, Task>? before = null) => this.before = before;

    public IReadOnlyList<IDomainEvent> Dispatched
    {
        get
        {
            lock (dispatched)
            {
                return dispatched.ToArray();
            }
        }
    }

    public async Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        if (before is not null)
        {
            await before(@event, cancellationToken);
        }
        lock (dispatched)
        {
            dispatched.Add(@event);
        }
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var @event in events)
        {
            await DispatchAsync(@event, cancellationToken);
        }
    }
}

public sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> entries = new();

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get
        {
            lock (entries)
            {
                return entries.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (entries)
        {
            entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}

public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset now;

    public FixedTimeProvider(DateTime utcNow) => now = new DateTimeOffset(utcNow, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;
}
