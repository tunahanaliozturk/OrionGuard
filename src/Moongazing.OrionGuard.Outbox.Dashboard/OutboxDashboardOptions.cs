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
    /// <c>RequireAuthorization(policyName)</c>. When null (default), the dashboard does
    /// NOT call <c>RequireAuthorization()</c> at all so the host's
    /// <c>AuthorizationOptions.FallbackPolicy</c> (if any) applies. If the host has no
    /// fallback policy and you rely on the dashboard being protected, set this to a
    /// concrete policy name. To opt the dashboard out of authorization entirely, set
    /// <see cref="AllowAnonymous"/> = <see langword="true"/>.
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
}
