using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

/// <summary>
/// EF Core <see cref="DbContextOptionsBuilder"/> extensions for OrionGuard.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Wires the <see cref="DomainEventSaveChangesInterceptor"/> into the DbContext's interceptor pipeline.
    /// Call inside <c>services.AddDbContext&lt;T&gt;((sp, o) =&gt; ...)</c> after <c>UseSqlServer/UseSqlite/etc.</c>,
    /// passing the resolution-time service provider so the interceptor can resolve scoped collaborators
    /// (<see cref="DomainEventCollector"/>, <see cref="OrionGuardEfCoreOptions"/>, and the registered
    /// <see cref="Moongazing.OrionGuard.Domain.Events.IDomainEventDispatcher"/>).
    /// </summary>
    /// <param name="builder">The options builder.</param>
    /// <param name="serviceProvider">
    /// The resolution-time service provider (the <c>sp</c> parameter of the
    /// <c>AddDbContext&lt;T&gt;((sp, o) =&gt; ...)</c> overload).
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static DbContextOptionsBuilder UseOrionGuardDomainEvents(
        this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        builder.AddInterceptors(new DomainEventSaveChangesInterceptor(serviceProvider));
        return builder;
    }

    /// <summary>
    /// Strongly-typed convenience overload that returns <see cref="DbContextOptionsBuilder{TContext}"/>
    /// so consumers can chain provider-specific Use* calls after it.
    /// </summary>
    /// <typeparam name="TContext">The consumer's <see cref="DbContext"/> type.</typeparam>
    /// <param name="builder">The options builder.</param>
    /// <param name="serviceProvider">
    /// The resolution-time service provider (the <c>sp</c> parameter of the
    /// <c>AddDbContext&lt;T&gt;((sp, o) =&gt; ...)</c> overload).
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> UseOrionGuardDomainEvents<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        IServiceProvider serviceProvider)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        builder.AddInterceptors(new DomainEventSaveChangesInterceptor(serviceProvider));
        return builder;
    }
}
