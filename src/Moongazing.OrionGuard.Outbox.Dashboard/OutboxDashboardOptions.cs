namespace Moongazing.OrionGuard.Outbox.Dashboard;

/// <summary>
/// Configures the read-only outbox dashboard surface registered by
/// <see cref="OutboxDashboardEndpointRouteBuilderExtensions.MapOutboxDashboard{TDbContext}"/>.
/// </summary>
public sealed class OutboxDashboardOptions
{
    /// <summary>
    /// URL prefix for the dashboard endpoint group. Default <c>/_orion/outbox</c>.
    /// The trailing slash is normalised; supplying <c>"/admin/outbox"</c> results in
    /// <c>/admin/outbox/failed</c> for the failed-messages listing.
    /// </summary>
    public string RoutePrefix { get; set; } = "/_orion/outbox";

    /// <summary>
    /// Authorization policy applied to the dashboard route group via
    /// <c>RequireAuthorization(policyName)</c>. When null (default), the host's
    /// <c>AuthorizationOptions.FallbackPolicy</c> applies if one is configured; otherwise
    /// the group calls <c>RequireAuthorization()</c> so the host's default policy (an
    /// authenticated user) applies. To opt the dashboard out of authorization entirely,
    /// set <see cref="AllowAnonymous"/> = <see langword="true"/>.
    /// </summary>
    public string? AuthorizationPolicyName { get; set; }

    /// <summary>
    /// Disable the default authorization requirement. Default <see langword="false"/>;
    /// production deployments should keep authorization on. The dashboard surfaces outbox
    /// row metadata (event types, error messages, correlation ids) which may be sensitive.
    /// </summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>
    /// Maximum number of rows returned by a single page. Default 100. Consumer-supplied
    /// page sizes are clamped to this ceiling so a single request cannot exhaust the
    /// connection's response buffer.
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Default page size when the consumer omits the <c>size</c> query string. Default 25.
    /// Capped at <see cref="MaxPageSize"/>.
    /// </summary>
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>
    /// Retry-count threshold for the failed listing. Rows where <c>RetryCount &gt;= </c> this
    /// value AND <c>ProcessedOnUtc</c> is null appear on the failed page. Default 3
    /// (matches the v6.5.0 default dispatcher retry budget).
    /// </summary>
    public int FailedRetryThreshold { get; set; } = 3;

    /// <summary>
    /// Maximum number of characters of <c>Error</c> returned by the failed listing.
    /// Default 1024. Full error text remains in the database; the listing truncates to
    /// keep response sizes bounded.
    /// </summary>
    public int ErrorTruncationLength { get; set; } = 1024;

    /// <summary>
    /// Enable the v6.5.5 mutation endpoints (<c>POST /{id}/replay</c> and
    /// <c>POST /{id}/discard</c>). Default <see langword="true"/>. Set false when the
    /// dashboard should remain strictly read-only (e.g., audit-only mounts).
    /// </summary>
    public bool EnableMutations { get; set; } = true;

    /// <summary>
    /// Require the <see cref="MutationHeaderName"/> header, with any non-empty value, on
    /// <c>POST /{id}/replay</c> and <c>POST /{id}/discard</c>; a request without it gets 400.
    /// Default <see langword="true"/>. The endpoints take no body, so without this check a page on another
    /// origin could send them with the operator's cookies as a cross-origin request the browser does not
    /// preflight. A custom header forces the preflight, which fails unless the host's CORS policy allows
    /// that origin and header. Set <see langword="false"/> only when every caller authenticates with a
    /// header (for example a bearer token) rather than a cookie.
    /// </summary>
    public bool RequireMutationHeader { get; set; } = true;

    /// <summary>
    /// Name of the header required by <see cref="RequireMutationHeader"/>. Default
    /// <c>X-OrionGuard-Dashboard</c>. It must start with <c>X-</c>, which is checked when the dashboard is
    /// mapped. The check only works with a header a cross-site page has to set itself, because that is what
    /// forces the CORS preflight; browsers attach a growing set of the others (<c>Cookie</c>, <c>Origin</c>,
    /// <c>DNT</c>, <c>Upgrade-Insecure-Requests</c>, <c>Sec-*</c>, the client hints) without being asked, and
    /// no browser adds an <c>X-</c> request header on its own.
    /// </summary>
    public string MutationHeaderName { get; set; } = "X-OrionGuard-Dashboard";

    /// <summary>
    /// Optional audit hook invoked after every successful replay / discard. The dashboard
    /// itself never writes audit rows so consumers stay in control of storage shape and
    /// retention. Throwing from the hook does NOT roll back the mutation; the database
    /// commit already happened. Wrap in try/catch if you want the endpoint to fail fast on
    /// audit failure.
    /// </summary>
    public Func<OutboxMutationEvent, Task>? OnMutation { get; set; }

    /// <summary>
    /// Default sort applied to the failed-listing when the consumer omits the
    /// <c>sort</c> query string. Default <see cref="OutboxFailedListingSort.OldestFirst"/>;
    /// most operators want to triage the longest-failing rows first.
    /// </summary>
    public OutboxFailedListingSort DefaultSort { get; set; } = OutboxFailedListingSort.OldestFirst;

    /// <summary>
    /// v6.5.15 security headers applied to every dashboard response. The defaults are
    /// production-friendly: X-Frame-Options=DENY (clickjacking guard),
    /// X-Content-Type-Options=nosniff (MIME-sniff guard), Referrer-Policy=no-referrer,
    /// Cache-Control=no-store. Set <see cref="SecurityHeaders"/> to an empty dictionary
    /// to disable all defaults; assign your own dictionary to merge custom headers.
    /// </summary>
    public IDictionary<string, string> SecurityHeaders { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["X-Frame-Options"] = "DENY",
        ["X-Content-Type-Options"] = "nosniff",
        ["Referrer-Policy"] = "no-referrer",
        ["Cache-Control"] = "no-store",
    };
}

/// <summary>
/// Sort options accepted by the failed-listing endpoint's <c>sort</c> query string. The
/// query string value matches the enum name case-insensitively (e.g. <c>?sort=newestfirst</c>
/// or <c>?sort=mostretries</c>).
/// </summary>
public enum OutboxFailedListingSort
{
    /// <summary>Order by <see cref="EntityFrameworkCore.Outbox.OutboxMessage.OccurredOnUtc"/> ascending - the dispatcher's natural FIFO order.</summary>
    OldestFirst = 0,

    /// <summary>Order by <see cref="EntityFrameworkCore.Outbox.OutboxMessage.OccurredOnUtc"/> descending - newest events first.</summary>
    NewestFirst = 1,

    /// <summary>Order by <see cref="EntityFrameworkCore.Outbox.OutboxMessage.RetryCount"/> descending - the most-retried rows first.</summary>
    MostRetries = 2,
}
