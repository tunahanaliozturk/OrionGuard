using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>
/// Default <see cref="IDomainEventDispatcher"/> that resolves <see cref="IDomainEventHandler{TEvent}"/>
/// instances from <see cref="IServiceProvider"/> and invokes them per <see cref="DomainEventDispatchOptions.Mode"/>.
/// </summary>
public sealed class ServiceProviderDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider serviceProvider;
    private readonly DomainEventDispatchOptions options;

    /// <summary>Initializes a new instance of the <see cref="ServiceProviderDomainEventDispatcher"/> class.</summary>
    /// <param name="serviceProvider">The DI container used to resolve <see cref="IDomainEventHandler{TEvent}"/> instances.</param>
    /// <param name="options">The dispatch options that select the handler invocation strategy.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public ServiceProviderDomainEventDispatcher(IServiceProvider serviceProvider, DomainEventDispatchOptions options)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Dispatches a single event to all registered handlers per the configured <see cref="DispatchMode"/>.</summary>
    /// <param name="event">The event instance to dispatch.</param>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>A task representing the asynchronous dispatch operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="event"/> is <see langword="null"/>.</exception>
    /// <exception cref="AggregateException">
    /// Thrown by <see cref="DispatchMode.SequentialContinueOnError"/> when one or more handlers fail; inner exceptions
    /// preserve the original handler errors in invocation order.
    /// </exception>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
        Justification = "Canonical DDD term aligned with public API contract from Task 3.")]
    [RequiresUnreferencedCode("ServiceProviderDomainEventDispatcher uses reflection to resolve IDomainEventHandler<TEvent>. Event types and their handler types must be preserved (e.g. via DynamicDependency or by rooting them in your application).")]
    [RequiresDynamicCode("MakeGenericType is used at dispatch time to construct IDomainEventHandler<TEvent>.")]
    public async Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(@event.GetType());
        var handlers = serviceProvider.GetServices(handlerType).Where(h => h is not null).ToArray();
        if (handlers.Length == 0)
        {
            return;
        }

        var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;

        switch (options.Mode)
        {
            case DispatchMode.SequentialFailFast:
                foreach (var handler in handlers)
                {
                    await InvokeAsync(method, handler!, @event, cancellationToken).ConfigureAwait(false);
                }
                break;

            case DispatchMode.SequentialContinueOnError:
                List<Exception>? errors = null;
                foreach (var handler in handlers)
                {
                    try
                    {
                        await InvokeAsync(method, handler!, @event, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        (errors ??= new List<Exception>()).Add(ex);
                    }
                }
                if (errors is { Count: > 0 })
                {
                    throw new AggregateException(errors);
                }
                break;

            case DispatchMode.Parallel:
                var parallelTasks = handlers.Select(h => InvokeAsync(method, h!, @event, cancellationToken)).ToArray();
                try
                {
                    await Task.WhenAll(parallelTasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    var faults = parallelTasks
                        .Where(t => t.IsFaulted)
                        .SelectMany(t => t.Exception!.InnerExceptions)
                        .ToArray();
                    if (faults.Length == 1)
                    {
                        ExceptionDispatchInfo.Capture(faults[0]).Throw();
                        throw; // unreachable; satisfies the compiler
                    }
                    throw new AggregateException(faults);
                }
                break;

            default:
                throw new InvalidOperationException($"Unknown DispatchMode '{options.Mode}'.");
        }
    }

    /// <summary>Dispatches a batch of events sequentially in iteration order.</summary>
    /// <param name="events">The events to dispatch.</param>
    /// <param name="cancellationToken">Token used to observe cancellation requests.</param>
    /// <returns>A task representing the asynchronous dispatch operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="events"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode("ServiceProviderDomainEventDispatcher uses reflection to resolve IDomainEventHandler<TEvent>. Event types and their handler types must be preserved (e.g. via DynamicDependency or by rooting them in your application).")]
    [RequiresDynamicCode("MakeGenericType is used at dispatch time to construct IDomainEventHandler<TEvent>.")]
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
        {
            await DispatchAsync(e, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InvokeAsync(MethodInfo method, object handler, IDomainEvent @event, CancellationToken ct)
    {
        Task task;
        try
        {
            task = (Task?)method.Invoke(handler, new object[] { @event, ct }) ?? Task.CompletedTask;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Surface the handler's original exception instead of the reflection wrapper
            // so callers (and DispatchMode policies) observe the real type. Using
            // ExceptionDispatchInfo preserves the original stack trace seamlessly.
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw; // unreachable; satisfies the compiler's return-path analysis.
        }

        await task.ConfigureAwait(false);
    }
}
