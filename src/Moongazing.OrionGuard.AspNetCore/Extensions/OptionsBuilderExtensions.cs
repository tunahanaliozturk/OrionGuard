using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moongazing.OrionGuard.AspNetCore.Options;

namespace Moongazing.OrionGuard.AspNetCore.Extensions;

/// <summary>
/// Extension methods for <see cref="OptionsBuilder{TOptions}"/> that integrate OrionGuard validation
/// into the ASP.NET Core options pipeline.
/// </summary>
public static class OptionsBuilderExtensions
{
    /// <summary>
    /// Validates options using OrionGuard validation attributes and any registered
    /// <see cref="Moongazing.OrionGuard.DependencyInjection.IValidator{T}"/> implementation.
    /// <para>
    /// Usage:
    /// <code>
    /// services.AddOptions&lt;MySettings&gt;()
    ///     .BindConfiguration("MySettings")
    ///     .ValidateWithOrionGuard();
    /// </code>
    /// </para>
    /// </summary>
    /// <typeparam name="TOptions">The options type to validate.</typeparam>
    /// <param name="builder">The options builder.</param>
    /// <returns>The <see cref="OptionsBuilder{TOptions}"/> for further chaining.</returns>
    public static OptionsBuilder<TOptions> ValidateWithOrionGuard<TOptions>(
        this OptionsBuilder<TOptions> builder) where TOptions : class
    {
        builder.Services.AddSingleton<IValidateOptions<TOptions>>(sp =>
            new OrionGuardOptionsValidation<TOptions>(sp));
        return builder;
    }

    /// <summary>
    /// Validates options using OrionGuard and also triggers validation eagerly at application startup.
    /// This ensures misconfigured options cause a fast failure rather than a runtime error on first access.
    /// <para>
    /// Usage:
    /// <code>
    /// services.AddOptions&lt;MySettings&gt;()
    ///     .BindConfiguration("MySettings")
    ///     .ValidateWithOrionGuardOnStart();
    /// </code>
    /// </para>
    /// </summary>
    /// <typeparam name="TOptions">The options type to validate.</typeparam>
    /// <param name="builder">The options builder.</param>
    /// <returns>The <see cref="OptionsBuilder{TOptions}"/> for further chaining.</returns>
    public static OptionsBuilder<TOptions> ValidateWithOrionGuardOnStart<TOptions>(
        this OptionsBuilder<TOptions> builder) where TOptions : class
    {
        builder.ValidateWithOrionGuard();
        builder.ValidateOnStart();
        return builder;
    }
}
