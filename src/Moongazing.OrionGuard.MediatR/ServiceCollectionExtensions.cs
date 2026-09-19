using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.MediatR;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers OrionGuard MediatR validation pipeline behaviors for requests and stream requests.
    /// Scans assemblies for IValidator implementations and registers ValidationBehavior and StreamValidationBehavior.
    /// </summary>
    public static IServiceCollection AddOrionGuardMediatR(this IServiceCollection services, params Assembly[] assemblies)
    {
        // Register the open generic pipeline behaviors
        services.AddTransient(typeof(global::MediatR.IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(global::MediatR.IStreamPipelineBehavior<,>), typeof(StreamValidationBehavior<,>));

        // Scan assemblies for IValidator<T> implementations
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Some types may fail to load (e.g. missing transitive dependencies).
                // Use the successfully loaded subset instead of crashing.
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            var validatorTypes = types
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .SelectMany(t => t.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>))
                    .Select(i => new { Interface = i, Implementation = t }));

            foreach (var validator in validatorTypes)
            {
                services.AddTransient(validator.Interface, validator.Implementation);
            }
        }

        return services;
    }
}
