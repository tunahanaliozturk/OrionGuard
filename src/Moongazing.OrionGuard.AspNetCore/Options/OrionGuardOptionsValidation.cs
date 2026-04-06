using Microsoft.Extensions.Options;
using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.AspNetCore.Options;

/// <summary>
/// Validates options using OrionGuard validation attributes and/or registered IValidator implementations.
/// Integrates with the <see cref="Microsoft.Extensions.Options.IValidateOptions{TOptions}"/> pipeline
/// so that configuration objects bound from appsettings.json are validated at resolve time (or at startup
/// when combined with <see cref="Extensions.OptionsBuilderExtensions.ValidateWithOrionGuardOnStart{TOptions}"/>).
/// </summary>
/// <typeparam name="TOptions">The options type to validate.</typeparam>
public sealed class OrionGuardOptionsValidation<TOptions> : IValidateOptions<TOptions> where TOptions : class
{
    private readonly IValidator<TOptions>? _validator;

    /// <summary>
    /// Initializes a new instance of <see cref="OrionGuardOptionsValidation{TOptions}"/>.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve an optional <see cref="IValidator{TOptions}"/>.</param>
    public OrionGuardOptionsValidation(IServiceProvider serviceProvider)
    {
        _validator = serviceProvider.GetService(typeof(IValidator<TOptions>)) as IValidator<TOptions>;
    }

    /// <summary>
    /// Validates the given options instance using attribute-based validation and, if registered,
    /// the <see cref="IValidator{TOptions}"/> implementation.
    /// </summary>
    /// <param name="name">The options name (used for named options).</param>
    /// <param name="options">The options instance to validate.</param>
    /// <returns>A <see cref="ValidateOptionsResult"/> indicating success or failure with error details.</returns>
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        // First: attribute-based validation ([NotNull], [NotEmpty], [Email], [Range], etc.)
        var attributeResult = AttributeValidator.Validate(options);

        // Second: IValidator<T> if one is registered in DI
        GuardResult? validatorResult = null;
        if (_validator is not null)
        {
            validatorResult = _validator.Validate(options);
        }

        // Combine both results when both are available
        var combined = validatorResult is not null
            ? GuardResult.Combine(attributeResult, validatorResult)
            : attributeResult;

        if (combined.IsValid)
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(combined.GetErrorSummary("; "));
    }
}
