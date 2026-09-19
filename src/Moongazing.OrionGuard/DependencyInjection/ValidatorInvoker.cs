using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.DependencyInjection;

/// <summary>
/// Runs every <see cref="IValidator{T}"/> registered for an object whose type is only known at runtime.
/// Transport integrations (SignalR, gRPC, MVC, Hangfire) use it so that resolution and invocation
/// behave the same everywhere.
/// </summary>
public static class ValidatorInvoker
{
    private static readonly MethodInfo ValidateTypedMethod = typeof(ValidatorInvoker).GetMethod(
        nameof(ValidateTypedAsync),
        BindingFlags.NonPublic | BindingFlags.Static)!;

    // Why: building the closed generic delegate costs reflection; doing it once per runtime type keeps
    // the per-call cost to a dictionary lookup and a direct delegate call.
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, object, ValidationContext?, CancellationToken, Task<GuardResult?>>> Invokers = new();

    /// <summary>
    /// Resolves every <see cref="IValidator{T}"/> registered for the runtime type of
    /// <paramref name="instance"/> from <paramref name="services"/> and runs them through
    /// <c>ValidateAsync</c>, so both synchronous and asynchronous rules run.
    /// </summary>
    /// <param name="services">
    /// The provider to resolve validators from. Pass the request or invocation scope so scoped
    /// validators, and validators with scoped dependencies, get a correct lifetime.
    /// </param>
    /// <param name="instance">The object to validate.</param>
    /// <param name="context">
    /// Optional context passed to <see cref="IValidator{T}.ValidateAsync(T, ValidationContext, CancellationToken)"/>.
    /// When <see langword="null"/>, the context-less overload is called.
    /// </param>
    /// <param name="cancellationToken">Token passed to each validator.</param>
    /// <returns>
    /// <see langword="null"/> when no validator is registered for the type; otherwise the result of the
    /// single validator, or the combined result of all of them in registration order.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="instance"/> is <see langword="null"/>.
    /// </exception>
    [RequiresUnreferencedCode("ValidatorInvoker resolves IValidator<T> for the runtime type of the instance via reflection. The instance and validator types must be preserved (e.g. via DynamicDependency or by rooting them in your application).")]
    [RequiresDynamicCode("MakeGenericMethod is used to build a typed invoker for the runtime type of the instance.")]
    public static Task<GuardResult?> ValidateAsync(
        IServiceProvider services,
        object instance,
        ValidationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(instance);

        var invoker = Invokers.GetOrAdd(instance.GetType(), CreateInvoker);
        return invoker(services, instance, context, cancellationToken);
    }

    [RequiresUnreferencedCode("Builds a typed invoker for a runtime type via MakeGenericMethod.")]
    [RequiresDynamicCode("MakeGenericMethod is used to build a typed invoker for a runtime type.")]
    private static Func<IServiceProvider, object, ValidationContext?, CancellationToken, Task<GuardResult?>> CreateInvoker(Type instanceType) =>
        ValidateTypedMethod
            .MakeGenericMethod(instanceType)
            .CreateDelegate<Func<IServiceProvider, object, ValidationContext?, CancellationToken, Task<GuardResult?>>>();

    private static async Task<GuardResult?> ValidateTypedAsync<T>(
        IServiceProvider services,
        object instance,
        ValidationContext? context,
        CancellationToken cancellationToken)
    {
        var value = (T)instance;
        List<GuardResult>? results = null;

        // Why: validators run one after another, not concurrently. Validators resolved from the same
        // scope can share scoped dependencies such as a DbContext, which does not allow concurrent use.
        foreach (var validator in services.GetServices<IValidator<T>>())
        {
            var result = context is null
                ? await validator.ValidateAsync(value, cancellationToken).ConfigureAwait(false)
                : await validator.ValidateAsync(value, context, cancellationToken).ConfigureAwait(false);
            (results ??= new List<GuardResult>()).Add(result);
        }

        return results switch
        {
            null => null,
            // Why: a single result is returned as is, so its SuggestedHttpStatusCode survives.
            { Count: 1 } => results[0],
            _ => GuardResult.Combine(results.ToArray()),
        };
    }
}
