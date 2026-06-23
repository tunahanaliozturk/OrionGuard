using System.Diagnostics.CodeAnalysis;
using global::Hangfire;
using global::Hangfire.Common;

namespace Moongazing.OrionGuard.Hangfire;

/// <summary>
/// Registration helpers that wire the <see cref="OrionGuardClientFilter"/> into Hangfire so that
/// background job arguments are validated at enqueue time using OrionGuard's validator resolution.
/// </summary>
public static class GlobalConfigurationExtensions
{
    /// <summary>
    /// Registers the OrionGuard client filter through Hangfire's fluent configuration. Call this inside
    /// the <c>GlobalConfiguration.Configuration.Use...()</c> chain during application startup.
    /// </summary>
    /// <param name="configuration">The Hangfire global configuration being built.</param>
    /// <param name="serviceProvider">
    /// The application's service provider, used by the filter to resolve <see cref="OrionGuard.DependencyInjection.IValidator{T}"/>
    /// instances for job arguments.
    /// </param>
    /// <returns>The same <see cref="IGlobalConfiguration"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configuration"/> or <paramref name="serviceProvider"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// GlobalConfiguration.Configuration
    ///     .UseSimpleAssemblyNameTypeSerializer()
    ///     .UseRecommendedSerializerSettings()
    ///     .UseInMemoryStorage()
    ///     .UseOrionGuardValidation(app.Services);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(
        "Registers OrionGuardClientFilter, which resolves IValidator<T> for runtime job-argument types via reflection.")]
    public static IGlobalConfiguration UseOrionGuardValidation(
        this IGlobalConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return configuration.UseFilter(new OrionGuardClientFilter(serviceProvider));
    }

    /// <summary>
    /// Adds the OrionGuard client filter to a Hangfire <see cref="JobFilterCollection"/>, typically
    /// <c>GlobalJobFilters.Filters</c>.
    /// </summary>
    /// <param name="filters">The filter collection to add the client filter to.</param>
    /// <param name="serviceProvider">
    /// The application's service provider, used by the filter to resolve validators for job arguments.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="filters"/> or <paramref name="serviceProvider"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// GlobalJobFilters.Filters.AddOrionGuardClientFilter(app.Services);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode(
        "Registers OrionGuardClientFilter, which resolves IValidator<T> for runtime job-argument types via reflection.")]
    public static void AddOrionGuardClientFilter(
        this JobFilterCollection filters,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        filters.Add(new OrionGuardClientFilter(serviceProvider));
    }
}
