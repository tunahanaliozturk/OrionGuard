namespace Moongazing.OrionGuard.Outbox.Dashboard;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>
/// Registers the read-only outbox dashboard route group on an
/// <see cref="IEndpointRouteBuilder"/>. Maps GET endpoints under a configurable
/// <see cref="OutboxDashboardOptions.RoutePrefix"/>; requires authorization by default.
/// </summary>
public static class OutboxDashboardEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Map the dashboard endpoints (currently a single failed-messages listing). The
    /// endpoint group is configured with <c>RequireAuthorization()</c> by default so the
    /// host's fallback policy applies; pass an explicit
    /// <see cref="OutboxDashboardOptions.AuthorizationPolicyName"/> to require a named
    /// policy, or set <see cref="OutboxDashboardOptions.AllowAnonymous"/> = <c>true</c>
    /// to opt out entirely (NOT recommended for production).
    /// </summary>
    /// <typeparam name="TDbContext">The consumer's <see cref="DbContext"/> that owns the outbox <see cref="DbSet{TEntity}"/>.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The route group convention builder so the caller can chain additional metadata.</returns>
    public static RouteGroupBuilder MapOutboxDashboard<TDbContext>(
        this IEndpointRouteBuilder endpoints,
        Action<OutboxDashboardOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = new OutboxDashboardOptions();
        configure?.Invoke(options);
        ValidateOptions(options);

        var group = endpoints.MapGroup(options.RoutePrefix.TrimEnd('/'));

        // Authorization wiring:
        //   AllowAnonymous   -> mark anonymous (NOT recommended in production).
        //   Named policy     -> evaluate that policy explicitly.
        //   Neither          -> do NOT call RequireAuthorization() so the host's
        //                       AuthorizationOptions.FallbackPolicy applies. Calling
        //                       RequireAuthorization() unconditionally would attach auth
        //                       metadata that bypasses a stricter FallbackPolicy (the host
        //                       fallback only fires for endpoints WITHOUT auth metadata).
        //                       Hosts that have no fallback policy and rely on the dashboard
        //                       being authorized should set AuthorizationPolicyName.
        if (options.AllowAnonymous)
        {
            group.AllowAnonymous();
        }
        else if (options.AuthorizationPolicyName is { } policy)
        {
            group.RequireAuthorization(policy);
        }
        // else: intentionally fall through so the host's FallbackPolicy applies.

        group.MapGet("/failed", async (TDbContext db, HttpContext http, int? page, int? size) =>
        {
            var pageNumber = page is null or < 1 ? 1 : page.Value;
            var pageSize = ResolvePageSize(size, options);
            var skip = (pageNumber - 1) * pageSize;
            var threshold = options.FailedRetryThreshold;
            var truncation = options.ErrorTruncationLength;

            // Include BOTH still-failing rows (Error set, not yet processed) AND rows that
            // the dispatcher dead-lettered (it stamps ProcessedOnUtc once RetryCount >=
            // MaxRetries while preserving Error/RetryCount for operator inspection). Filtering
            // on Error != null AND RetryCount >= threshold covers both states without
            // double-counting successfully-dispatched rows (those have Error == null).
            var query = db.Set<OutboxMessage>()
                .AsNoTracking()
                .Where(m => m.RetryCount >= threshold && m.Error != null)
                .OrderBy(m => m.OccurredOnUtc);

            var total = await query.CountAsync(http.RequestAborted).ConfigureAwait(false);
            var rows = await query
                .Skip(skip)
                .Take(pageSize)
                .Select(m => new OutboxFailedMessageRow(
                    m.Id,
                    m.EventType,
                    m.OccurredOnUtc,
                    m.RetryCount,
                    m.Error == null
                        ? null
                        : m.Error.Length <= truncation ? m.Error : m.Error.Substring(0, truncation),
                    m.CorrelationId))
                .ToListAsync(http.RequestAborted)
                .ConfigureAwait(false);

            return Results.Ok(new
            {
                page = pageNumber,
                size = pageSize,
                total,
                items = rows,
            });
        });

        return group;
    }

    private static int ResolvePageSize(int? requested, OutboxDashboardOptions options)
    {
        var size = requested is null or < 1 ? options.DefaultPageSize : requested.Value;
        return size > options.MaxPageSize ? options.MaxPageSize : size;
    }

    private static void ValidateOptions(OutboxDashboardOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RoutePrefix))
        {
            throw new InvalidOperationException("OutboxDashboardOptions.RoutePrefix must be non-empty.");
        }
        if (options.MaxPageSize < 1)
        {
            throw new InvalidOperationException("OutboxDashboardOptions.MaxPageSize must be at least 1.");
        }
        if (options.DefaultPageSize < 1)
        {
            throw new InvalidOperationException("OutboxDashboardOptions.DefaultPageSize must be at least 1.");
        }
        if (options.FailedRetryThreshold < 1)
        {
            throw new InvalidOperationException("OutboxDashboardOptions.FailedRetryThreshold must be at least 1.");
        }
        if (options.ErrorTruncationLength < 0)
        {
            throw new InvalidOperationException("OutboxDashboardOptions.ErrorTruncationLength must be non-negative.");
        }
    }
}
