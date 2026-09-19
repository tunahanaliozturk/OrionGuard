namespace Moongazing.OrionGuard.Aspire;

/// <summary>
/// Options for <see cref="OrionGuardAspireExtensions.AddOrionGuardDefaults{TBuilder}"/>. A health check is only added
/// when the package that owns it is referenced.
/// </summary>
public sealed class OrionGuardAspireOptions
{
    /// <summary>
    /// Skips the validation infrastructure health check (<c>OrionGuardHealthCheck</c> from OrionGuard.AspNetCore),
    /// which is otherwise registered whenever OrionGuard.AspNetCore is referenced. It never reports worse than
    /// Degraded, so it is on by default.
    /// </summary>
    public bool DisableValidationHealthCheck { get; set; }

    /// <summary>
    /// Registers the outbox archival health check (<c>OutboxArchivalHealthCheck</c> from OrionGuard.EntityFrameworkCore)
    /// in services that enable archival with <c>UseOutboxArchival()</c>; services without archival are skipped.
    /// </summary>
    /// <remarks>
    /// Off by default because the check's default thresholds (Degraded after 5 minutes, Unhealthy after 15) are
    /// shorter than archival's default one-hour polling interval, so turning it on unchanged would fail readiness
    /// between batches. Register an <c>OutboxArchivalHealthCheckOptions</c> singleton with thresholds above your
    /// <c>PollingInterval</c> when you enable it.
    /// </remarks>
    public bool EnableOutboxArchivalHealthCheck { get; set; }
}
