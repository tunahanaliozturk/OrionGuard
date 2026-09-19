using global::MassTransit;

namespace Moongazing.OrionGuard.MassTransit;

/// <summary>
/// Registration helpers that add <see cref="OrionGuardConsumeFilter{TMessage}"/> to a MassTransit
/// consume pipe.
/// </summary>
public static class ConsumePipeConfiguratorExtensions
{
    /// <summary>
    /// Adds OrionGuard validation as a scoped consume filter for every message type consumed through
    /// <paramref name="configurator"/>.
    /// </summary>
    /// <remarks>
    /// Call it on the bus configurator to validate on every receive endpoint, or on a single receive
    /// endpoint configurator to validate only there.
    /// </remarks>
    /// <param name="configurator">The bus or receive endpoint configurator.</param>
    /// <param name="context">
    /// The registration context passed to the <c>UsingXxx((context, cfg) =&gt; ...)</c> callback. MassTransit
    /// uses it to create the filter, and so the validators, from each message's consume scope.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configurator"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// <code>
    /// x.UsingRabbitMq((context, cfg) =>
    /// {
    ///     cfg.UseOrionGuardValidation(context);
    ///     cfg.ConfigureEndpoints(context);
    /// });
    /// </code>
    /// </example>
    public static void UseOrionGuardValidation(
        this IConsumePipeConfigurator configurator,
        IRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(context);

        configurator.UseConsumeFilter(typeof(OrionGuardConsumeFilter<>), context);
    }
}
