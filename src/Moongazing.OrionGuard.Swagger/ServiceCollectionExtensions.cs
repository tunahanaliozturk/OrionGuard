using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Moongazing.OrionGuard.Swagger;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds OrionGuard schema filter to Swagger/OpenAPI generation.
    /// </summary>
    public static IServiceCollection AddOrionGuardSwagger(this IServiceCollection services)
    {
        services.Configure<SwaggerGenOptions>(options =>
        {
            options.SchemaFilter<OrionGuardSchemaFilter>();
        });

        return services;
    }
}
