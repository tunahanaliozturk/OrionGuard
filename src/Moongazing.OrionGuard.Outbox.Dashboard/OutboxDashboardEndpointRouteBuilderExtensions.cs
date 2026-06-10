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

        group.MapGet("/failed", async (TDbContext db, HttpContext http, int? page, int? size, string? sort) =>
        {
            var pageNumber = page is null or < 1 ? 1 : page.Value;
            var pageSize = ResolvePageSize(size, options);
            var skip = (pageNumber - 1) * pageSize;
            var threshold = options.FailedRetryThreshold;
            var truncation = options.ErrorTruncationLength;
            var sortOrder = ResolveSort(sort, options.DefaultSort);

            // Include BOTH still-failing rows (Error set, not yet processed) AND rows that
            // the dispatcher dead-lettered (it stamps ProcessedOnUtc once RetryCount >=
            // MaxRetries while preserving Error/RetryCount for operator inspection). Filtering
            // on Error != null AND RetryCount >= threshold covers both states without
            // double-counting successfully-dispatched rows (those have Error == null).
            var baseQuery = db.Set<OutboxMessage>()
                .AsNoTracking()
                .Where(m => m.RetryCount >= threshold && m.Error != null);

            IQueryable<OutboxMessage> ordered = sortOrder switch
            {
                OutboxFailedListingSort.NewestFirst => baseQuery.OrderByDescending(m => m.OccurredOnUtc),
                OutboxFailedListingSort.MostRetries => baseQuery
                    .OrderByDescending(m => m.RetryCount)
                    .ThenBy(m => m.OccurredOnUtc),
                _ => baseQuery.OrderBy(m => m.OccurredOnUtc),
            };

            var total = await baseQuery.CountAsync(http.RequestAborted).ConfigureAwait(false);
            var rows = await ordered
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

            var totalPages = (int)Math.Ceiling((double)total / pageSize);
            return Results.Ok(new
            {
                page = pageNumber,
                size = pageSize,
                total,
                totalPages,
                hasNextPage = pageNumber < totalPages,
                hasPreviousPage = pageNumber > 1,
                sort = sortOrder.ToString(),
                items = rows,
            });
        });

        group.MapGet("/failed/cursor", async (TDbContext db, HttpContext http, string? cursor, int? size, string? sort) =>
        {
            var pageSize = ResolvePageSize(size, options);
            var threshold = options.FailedRetryThreshold;
            var truncation = options.ErrorTruncationLength;

            // Sort axis is taken from the cursor when present (a switched sort mid-paging
            // would otherwise emit duplicate / skipped rows). If no cursor is supplied,
            // fall through to the query-string / default like the offset endpoint.
            OutboxFailedCursor.TryDecode(cursor, out var decoded);
            var sortOrder = cursor is not null && OutboxFailedCursor.TryDecode(cursor, out var fromCursor)
                ? fromCursor.Sort
                : ResolveSort(sort, options.DefaultSort);

            var baseQuery = db.Set<OutboxMessage>()
                .AsNoTracking()
                .Where(m => m.RetryCount >= threshold && m.Error != null);

            // Apply cursor predicate to the WHERE so the database does a keyset seek
            // instead of an OFFSET scan. Each branch repeats the secondary key (Id) as a
            // stable tiebreaker so rows with identical sort values do not slip through.
            if (cursor is not null && OutboxFailedCursor.TryDecode(cursor, out var c))
            {
                var lastOccurred = new DateTime(c.LastOccurredOnUtcTicks, DateTimeKind.Utc);
                var lastId = c.LastId;
                var lastRetries = c.LastRetryCount;
                baseQuery = sortOrder switch
                {
                    OutboxFailedListingSort.NewestFirst => baseQuery.Where(m =>
                        m.OccurredOnUtc < lastOccurred
                        || (m.OccurredOnUtc == lastOccurred && m.Id.CompareTo(lastId) > 0)),
                    OutboxFailedListingSort.MostRetries => baseQuery.Where(m =>
                        m.RetryCount < lastRetries
                        || (m.RetryCount == lastRetries && m.OccurredOnUtc > lastOccurred)
                        || (m.RetryCount == lastRetries && m.OccurredOnUtc == lastOccurred && m.Id.CompareTo(lastId) > 0)),
                    _ => baseQuery.Where(m =>
                        m.OccurredOnUtc > lastOccurred
                        || (m.OccurredOnUtc == lastOccurred && m.Id.CompareTo(lastId) > 0)),
                };
            }

            IQueryable<OutboxMessage> ordered = sortOrder switch
            {
                OutboxFailedListingSort.NewestFirst => baseQuery
                    .OrderByDescending(m => m.OccurredOnUtc).ThenBy(m => m.Id),
                OutboxFailedListingSort.MostRetries => baseQuery
                    .OrderByDescending(m => m.RetryCount)
                    .ThenBy(m => m.OccurredOnUtc)
                    .ThenBy(m => m.Id),
                _ => baseQuery.OrderBy(m => m.OccurredOnUtc).ThenBy(m => m.Id),
            };

            // Take one extra row to detect whether another page exists without an extra
            // round-trip; the response slices off the peek before serialisation.
            var page = await ordered
                .Take(pageSize + 1)
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

            string? nextCursor = null;
            if (page.Count > pageSize)
            {
                // Peeked one ahead - slice it off and base the next cursor on the LAST
                // returned row (not the peek row), which is the one the client will see
                // as their "last" position.
                page.RemoveAt(page.Count - 1);
                if (page.Count > 0)
                {
                    var last = page[^1];
                    nextCursor = new OutboxFailedCursor(
                        last.OccurredOnUtc.Ticks,
                        last.Id,
                        last.RetryCount,
                        sortOrder).Encode();
                }
            }

            return Results.Ok(new
            {
                size = pageSize,
                sort = sortOrder.ToString(),
                items = page,
                nextCursor,
                hasNextPage = nextCursor is not null,
            });
        });

        if (options.EnableMutations)
        {
            // Replay: clear RetryCount + Error so the next dispatcher pass re-attempts the
            // row. ProcessedOnUtc stays null (or is cleared if the dispatcher already
            // dead-lettered). Returns 200 on success, 404 if the id is unknown.
            group.MapPost("/{id:guid}/replay",
                async (TDbContext db, HttpContext http, Guid id) =>
                {
                    var row = await db.Set<OutboxMessage>()
                        .FirstOrDefaultAsync(m => m.Id == id, http.RequestAborted)
                        .ConfigureAwait(false);
                    if (row is null)
                    {
                        return Results.NotFound();
                    }
                    // Reject replay for cleanly-processed rows (Error null AND already
                    // processed). Without this guard a caller could clear ProcessedOnUtc
                    // on a successful event, causing the dispatcher to re-dispatch it as
                    // if the original handler never ran. Failed + dead-lettered rows
                    // (Error != null) remain replayable - that's the whole point.
                    if (row.ProcessedOnUtc is not null && row.Error is null)
                    {
                        return Results.Conflict(new
                        {
                            id,
                            error = "already-processed-success",
                            message = "Row was dispatched successfully and has no error; replay would re-deliver a clean event.",
                        });
                    }
                    row.RetryCount = 0;
                    row.Error = null;
                    row.ProcessedOnUtc = null;
                    await db.SaveChangesAsync(http.RequestAborted).ConfigureAwait(false);

                    if (options.OnMutation is { } hook)
                    {
                        await hook(new OutboxMutationEvent(
                            Action: "replay",
                            OutboxMessageId: id,
                            HttpContext: http,
                            OccurredAtUtc: DateTime.UtcNow)).ConfigureAwait(false);
                    }
                    return Results.Ok(new { id, action = "replay" });
                });

            // Discard: mark the row processed without re-dispatch. ProcessedOnUtc is set so
            // the dispatcher loop skips it; Error/RetryCount stay intact so future operators
            // can still see what failed. Returns 200 on success, 404 if the id is unknown.
            group.MapPost("/{id:guid}/discard",
                async (TDbContext db, HttpContext http, Guid id) =>
                {
                    var row = await db.Set<OutboxMessage>()
                        .FirstOrDefaultAsync(m => m.Id == id, http.RequestAborted)
                        .ConfigureAwait(false);
                    if (row is null)
                    {
                        return Results.NotFound();
                    }
                    if (row.ProcessedOnUtc is not null)
                    {
                        // Already processed (either by successful dispatch or a prior discard).
                        // Idempotent: report 200 without re-stamping so audit hooks don't fire
                        // again.
                        return Results.Ok(new { id, action = "discard", note = "already processed" });
                    }
                    row.ProcessedOnUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(http.RequestAborted).ConfigureAwait(false);

                    if (options.OnMutation is { } hook)
                    {
                        await hook(new OutboxMutationEvent(
                            Action: "discard",
                            OutboxMessageId: id,
                            HttpContext: http,
                            OccurredAtUtc: DateTime.UtcNow)).ConfigureAwait(false);
                    }
                    return Results.Ok(new { id, action = "discard" });
                });
        }

        return group;
    }

    private static int ResolvePageSize(int? requested, OutboxDashboardOptions options)
    {
        var size = requested is null or < 1 ? options.DefaultPageSize : requested.Value;
        return size > options.MaxPageSize ? options.MaxPageSize : size;
    }

    private static OutboxFailedListingSort ResolveSort(string? requested, OutboxFailedListingSort fallback)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return fallback;
        }
        // Enum.TryParse with ignoreCase=true also accepts numeric strings ("99") and
        // returns Success when the value can be cast to the enum's underlying type. That
        // would let a misconfigured caller smuggle an undefined sort through the response.
        // Pair the parse with Enum.IsDefined so only declared names succeed.
        return Enum.TryParse<OutboxFailedListingSort>(requested, ignoreCase: true, out var parsed)
               && Enum.IsDefined(typeof(OutboxFailedListingSort), parsed)
            ? parsed
            : fallback;
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
