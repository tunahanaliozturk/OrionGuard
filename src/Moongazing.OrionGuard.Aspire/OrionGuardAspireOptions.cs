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
    /// Thresholds left unset scale with the archival polling interval - Degraded after two intervals, Unhealthy
    /// after three, never below 5 and 15 minutes - so the default one-hour interval gives two and three hours and
    /// the check does not flap between batches. Set them explicitly on an <c>OutboxArchivalHealthCheckOptions</c>
    /// singleton only to tighten them. This is off by default because it reports on a background maintenance job:
    /// once it is part of the readiness report, archival falling behind takes the service out of rotation.
    /// </remarks>
    public bool EnableOutboxArchivalHealthCheck { get; set; }
}
