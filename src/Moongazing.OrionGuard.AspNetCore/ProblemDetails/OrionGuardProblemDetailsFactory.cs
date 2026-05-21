using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Domain.Exceptions;

namespace Moongazing.OrionGuard.AspNetCore.ProblemDetails;

/// <summary>
/// Factory for converting <see cref="GuardResult"/> instances into
/// RFC 9457 compliant <see cref="ValidationProblemDetails"/> responses.
/// </summary>
public static class OrionGuardProblemDetailsFactory
{
    private const string ProblemDetailsType = "https://tools.ietf.org/html/rfc9457";
    private const string DefaultTitle = "Validation Failed";
    private const int DefaultStatusCode = 422;

    private const string BusinessRuleProblemType =
        "https://moongazing.dev/orionguard/problems/business-rule-violation";
    private const string BusinessRuleTitle = "Business Rule Violation";

    /// <summary>
    /// Creates a <see cref="ValidationProblemDetails"/> from a failed <see cref="GuardResult"/>.
    /// </summary>
    /// <param name="result">The validation result containing errors.</param>
    /// <returns>A <see cref="ValidationProblemDetails"/> ready for serialization.</returns>
    public static ValidationProblemDetails Create(GuardResult result)
    {
        var errors = result.ToErrorDictionary();
        var problemDetails = new ValidationProblemDetails(errors)
        {
            Type = ProblemDetailsType,
            Title = DefaultTitle,
            Status = result.SuggestedHttpStatusCode ?? DefaultStatusCode
        };

        return problemDetails;
    }

    /// <summary>
    /// Creates a <see cref="ValidationProblemDetails"/> from an <see cref="AggregateValidationException"/>.
    /// </summary>
    /// <param name="exception">The aggregate validation exception.</param>
    /// <returns>A <see cref="ValidationProblemDetails"/> ready for serialization.</returns>
    public static ValidationProblemDetails Create(AggregateValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => e.ParameterName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());

        var problemDetails = new ValidationProblemDetails(errors)
        {
            Type = ProblemDetailsType,
            Title = DefaultTitle,
            Status = DefaultStatusCode
        };

        return problemDetails;
    }

    /// <summary>
    /// Creates a <see cref="ValidationProblemDetails"/> from a <see cref="BusinessRuleValidationException"/>.
    /// Errors are keyed by the rule's CLR type name; status defaults to 422.
    /// </summary>
    /// <param name="exception">The business rule exception.</param>
    /// <returns>A <see cref="ValidationProblemDetails"/> ready for serialization.</returns>
    public static ValidationProblemDetails Create(BusinessRuleValidationException exception)
    {
        var errors = new Dictionary<string, string[]>
        {
            [exception.RuleName] = new[] { exception.Message },
        };

        return new ValidationProblemDetails(errors)
        {
            Type = BusinessRuleProblemType,
            Title = BusinessRuleTitle,
            Status = StatusCodes.Status422UnprocessableEntity,
        };
    }
}
