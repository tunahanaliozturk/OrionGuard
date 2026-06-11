namespace Moongazing.OrionGuard.Outbox.Dashboard;

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

/// <summary>
/// v6.5.15 IApplicationBuilder extension that registers a middleware writing the
/// configured security headers on EVERY response for paths under the dashboard's route
/// prefix. The endpoint filter shipped with <see cref="OutboxDashboardEndpointRouteBuilderExtensions.MapOutboxDashboard{TDbContext}"/>
/// only fires for responses the dashboard handler produces; this middleware is the path
/// for consumers who want the hardening defaults to also stamp 401 / 403 / 404 responses
/// short-circuited by authentication / authorization before the endpoint handler runs.
/// </summary>
public static class OutboxDashboardSecurityHeadersMiddleware
{
    /// <summary>
    /// Wire the security-headers middleware in the consumer's pipeline. Place it BEFORE
    /// <c>UseAuthentication()</c> / <c>UseAuthorization()</c> so the headers also land on
    /// auth-short-circuited responses.
    /// </summary>
    public static IApplicationBuilder UseOutboxDashboardSecurityHeaders(
        this IApplicationBuilder app, string routePrefix, IDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrEmpty(routePrefix);
        ArgumentNullException.ThrowIfNull(headers);

        var prefix = routePrefix.TrimEnd('/');
        // Snapshot the headers so a later mutation on the caller's dictionary does not
        // affect the middleware's behaviour after registration.
        var snapshot = new Dictionary<string, string>(headers, StringComparer.Ordinal);

        return app.Use(async (HttpContext ctx, RequestDelegate next) =>
        {
            if (ctx.Request.Path.StartsWithSegments(prefix))
            {
                ctx.Response.OnStarting(() =>
                {
                    foreach (var (k, v) in snapshot)
                    {
                        ctx.Response.Headers[k] = v;
                    }
                    return Task.CompletedTask;
                });
            }
            await next(ctx).ConfigureAwait(false);
        });
    }
}
