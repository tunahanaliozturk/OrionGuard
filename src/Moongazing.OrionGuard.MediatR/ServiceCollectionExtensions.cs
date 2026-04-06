using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.MediatR;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers OrionGuard MediatR validation pipeline behavior.
    /// Scans assemblies for IValidator implementations and registers ValidationBehavior.
    /// </summary>
    public static IServiceCollection AddOrionGuardMediatR(this IServiceCollection services, params Assembly[] assemblies)
    {
        // Register the open generic pipeline behavior
        services.AddTransient(typeof(global::MediatR.IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // Scan assemblies for IValidator<T> implementations
        foreach (var assembly in assemblies)
        {
            var validatorTypes = assembly.GetTypes()
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
