using Microsoft.AspNetCore.Http;

namespace Moongazing.OrionGuard.AspNetCore.Options;

/// <summary>
/// Configuration options for OrionGuard ASP.NET Core integration.
/// </summary>
public sealed class OrionGuardAspNetCoreOptions
{
    /// <summary>
    /// When true, validation failures are returned as RFC 9457 ProblemDetails responses.
    /// Default is <c>true</c>.
    /// </summary>
    public bool UseProblemDetails { get; set; } = true;

    /// <summary>
    /// The default HTTP status code for validation failures.
    /// Default is <c>422</c> (Unprocessable Entity).
    /// </summary>
    public int DefaultStatusCode { get; set; } = 422;

    /// <summary>
    /// When true, suppresses the built-in ASP.NET Core model state invalid filter
    /// so OrionGuard handles all validation responses.
    /// Default is <c>false</c>.
    /// </summary>
    public bool SuppressModelStateInvalidFilter { get; set; } = false;

    /// <summary>
    /// HTTP status code returned for <see cref="Moongazing.OrionGuard.Domain.Exceptions.BusinessRuleValidationException"/>.
    /// Defaults to <see cref="StatusCodes.Status422UnprocessableEntity"/> (RFC 9457 — request is
    /// syntactically valid but semantically rejected). Override (e.g., to 400) for legacy clients.
    /// </summary>
    public int BusinessRuleStatusCode { get; set; } = StatusCodes.Status422UnprocessableEntity;
}
