using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Moongazing.OrionGuard.DependencyInjection;

/// <summary>
/// Registers source-generated EF Core value converters emitted by the
/// <c>[StronglyTypedId]</c> generator.
/// </summary>
public static class StronglyTypedIdServiceExtensions
{
    private const string ConverterSuffix = "EfCoreValueConverter";

    /// <summary>
    /// Scans the specified assemblies for generated EF Core value converters (named by the
    /// <c>[StronglyTypedId]</c> generator with the suffix <c>EfCoreValueConverter</c>) and
    /// registers each as a singleton of its concrete type.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Assemblies to scan. If none are supplied, the calling assembly is used.</param>
    /// <returns>The <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddOrionGuardStronglyTypedIds(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (assemblies is null || assemblies.Length == 0)
        {
            assemblies = new[] { Assembly.GetCallingAssembly() };
        }

        foreach (var assembly in assemblies)
        {
            var converters = assembly.GetTypes()
                .Where(t => !t.IsAbstract
                            && !t.IsGenericTypeDefinition
                            && t.Name.EndsWith(ConverterSuffix, StringComparison.Ordinal));

            foreach (var converter in converters)
            {
                services.TryAddSingleton(converter);
            }
        }

        return services;
    }
}
