namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Fluent builder returned from <c>RuleFor</c> calls on
/// <see cref="DependencyInjection.AbstractValidator{T}"/>. Allows chaining modifiers
/// (severity, error code) without interrupting the rule registration fluency.
/// </summary>
/// <remarks>
/// Using an interface keeps the concrete rule representation internal while still
/// exposing a stable, mockable extension point. The builder itself is mutable but
/// scoped to the rule it was created for.
/// </remarks>
public interface IRuleBuilder
{
    /// <summary>
    /// Sets the severity for the rule's <see cref="ValidationError"/> output.
    /// </summary>
    /// <remarks>
    /// Warnings and infos do not contribute to <see cref="GuardResult.IsInvalid"/>; they
    /// appear in <see cref="GuardResult.Warnings"/> / <see cref="GuardResult.Infos"/> for
    /// logging, telemetry, or soft UI surfacing.
    /// </remarks>
    IRuleBuilder WithSeverity(Severity severity);

    /// <summary>
    /// Sets the error code emitted on the rule's <see cref="ValidationError"/>.
    /// Useful for clients that react to failures programmatically.
    /// </summary>
    IRuleBuilder WithErrorCode(string errorCode);

    /// <summary>
    /// Marks an async rule as safe to run in parallel with other <c>Parallel</c>-marked
    /// async rules in the same rule set. Only affects
    /// <see cref="DependencyInjection.AbstractValidator{T}.ValidateAsync(T, System.Threading.CancellationToken)"/>
    /// and overloads; sync rules always run sequentially.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>When to use.</b> Async rules that hit independent I/O resources -- DB uniqueness
    /// checks, external service calls, third-party API lookups. Rules that share mutable
    /// state or order-dependent side effects should <i>not</i> be marked parallel.
    /// </para>
    /// <para>
    /// <b>Batching.</b> Within a single rule set, consecutive async rules marked
    /// <c>Parallel</c> are batched and awaited via <see cref="Task.WhenAll(Task[])"/>.
    /// A non-parallel rule or the start of the next rule set flushes the current batch.
    /// </para>
    /// </remarks>
    IRuleBuilder Parallel();
}
