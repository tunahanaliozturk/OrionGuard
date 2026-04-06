using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Moongazing.OrionGuard.AspNetCore.Filters;

namespace Moongazing.OrionGuard.AspNetCore.Extensions;

/// <summary>
/// Extension methods for adding OrionGuard validation to Minimal API endpoints.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Adds OrionGuard validation as an endpoint filter for the specified request type.
    /// The filter resolves <c>IValidator&lt;TRequest&gt;</c> from DI and validates the
    /// matching argument before the endpoint handler executes.
    /// </summary>
    /// <typeparam name="TRequest">The request type to validate.</typeparam>
    /// <param name="builder">The route handler builder.</param>
    /// <returns>The <see cref="RouteHandlerBuilder"/> for further chaining.</returns>
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder) where TRequest : class
        => builder.AddEndpointFilter<OrionGuardEndpointFilter<TRequest>>();
}
