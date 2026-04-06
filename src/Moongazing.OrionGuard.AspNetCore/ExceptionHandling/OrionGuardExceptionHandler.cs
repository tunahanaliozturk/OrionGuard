using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moongazing.OrionGuard.AspNetCore.ProblemDetails;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Exceptions;

namespace Moongazing.OrionGuard.AspNetCore.ExceptionHandling;

/// <summary>
/// Global exception handler that converts OrionGuard exceptions into
/// RFC 9457 compliant ProblemDetails HTTP responses.
/// Handles <see cref="AggregateValidationException"/> and <see cref="GuardException"/>.
/// </summary>
public sealed class OrionGuardExceptionHandler : IExceptionHandler
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<OrionGuardExceptionHandler> _logger;

    public OrionGuardExceptionHandler(ILogger<OrionGuardExceptionHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is AggregateValidationException aggregateException)
        {
            _logger.LogWarning(
                aggregateException,
                "Validation failed with {ErrorCount} error(s)",
                aggregateException.Errors.Count);

            var problemDetails = OrionGuardProblemDetailsFactory.Create(aggregateException);

            httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status422UnprocessableEntity;
            httpContext.Response.ContentType = MediaTypeNames.Application.Json;

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return true;
        }

        if (exception is GuardException guardException)
        {
            _logger.LogWarning(
                guardException,
                "Guard validation failed for parameter '{ParameterName}'",
                guardException.ParameterName);

            var errors = new Dictionary<string, string[]>();

            if (!string.IsNullOrEmpty(guardException.ParameterName))
            {
                errors[guardException.ParameterName] = [guardException.Message];
            }
            else
            {
                errors[""] = [guardException.Message];
            }

            var problemDetails = new ValidationProblemDetails(errors)
            {
                Type = "https://tools.ietf.org/html/rfc9457",
                Title = "Validation Failed",
                Status = StatusCodes.Status400BadRequest
            };

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            httpContext.Response.ContentType = MediaTypeNames.Application.Json;

            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return true;
        }

        return false;
    }
}
