using Microsoft.AspNetCore.Builder;

namespace Moongazing.OrionGuard.AspNetCore.Extensions;

/// <summary>
/// Extension methods for configuring OrionGuard middleware in the ASP.NET Core pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds OrionGuard validation exception handling middleware to the pipeline.
    /// This middleware converts <c>GuardException</c> and <c>AggregateValidationException</c>
    /// into RFC 9457 ProblemDetails responses automatically.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> for further chaining.</returns>
    public static IApplicationBuilder UseOrionGuardValidation(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        return app;
    }
}
