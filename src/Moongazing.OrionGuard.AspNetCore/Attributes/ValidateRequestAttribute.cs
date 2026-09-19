using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.AspNetCore.Filters;

namespace Moongazing.OrionGuard.AspNetCore.Attributes;

/// <summary>
/// Marks an MVC controller or action for automatic OrionGuard validation.
/// The attribute adds <see cref="OrionGuardMvcFilter"/> to the action's filter pipeline, which runs
/// every <c>IValidator&lt;T&gt;</c> registered for each action argument before the action executes.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ValidateRequestAttribute : Attribute, IFilterFactory
{
    /// <inheritdoc />
    public bool IsReusable => false;

    /// <inheritdoc />
    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        // Why: the filter has no dependencies, so it is created directly when AddOrionGuardAspNetCore()
        // did not register it, instead of failing every request that reaches the action.
        ActivatorUtilities.GetServiceOrCreateInstance<OrionGuardMvcFilter>(serviceProvider);
}
