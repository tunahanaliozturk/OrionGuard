using System.Collections.Concurrent;
using MassTransit;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.MassTransit.Tests;

/// <summary>Message with a registered validator.</summary>
public sealed record SubmitOrder(string OrderId, string CustomerEmail);

/// <summary>Message whose type has no registered validator; must reach its consumer untouched.</summary>
public sealed record UnvalidatedMessage(string Anything);

/// <summary>Message validated only by an async rule (no synchronous rules at all).</summary>
public sealed record AsyncOnlyMessage(string Token);

/// <summary>Message whose validator depends on a scoped service.</summary>
public sealed record ScopedMessage(string Value);

/// <summary>Message with two independent validators registered for it.</summary>
public sealed record MultiValidatedMessage(string First, string Second);

/// <summary>
/// Interface message contract. MassTransit deserializes interface contracts into a generated
/// implementation type, so the runtime type of <c>context.Message</c> is not <see cref="IShipOrder"/>.
/// </summary>
public interface IShipOrder
{
    string OrderId { get; }
}

public sealed class SubmitOrderValidator : AbstractValidator<SubmitOrder>
{
    public SubmitOrderValidator(InvocationCounter counter)
    {
        counter.Increment();
        RuleFor(x => x.OrderId, "OrderId", p => p.NotEmpty());
        RuleFor(x => x.CustomerEmail, "CustomerEmail", p => p.NotEmpty().Email());
    }
}

/// <summary>
/// Declares only an async rule, so it is a no-op under the synchronous <c>Validate(T)</c> overload. It
/// proves the filter runs the async pipeline. The token "valid" passes; anything else fails.
/// </summary>
public sealed class AsyncOnlyMessageValidator : AbstractValidator<AsyncOnlyMessage>
{
    public AsyncOnlyMessageValidator()
    {
        RuleForAsync(
            async x =>
            {
                await Task.Yield();
                return x.Token == "valid";
            },
            "Token failed async validation.",
            "Token");
    }
}

/// <summary>
/// A scoped dependency. With <c>ValidateScopes</c> on, resolving it from the root provider throws, so a
/// successful validation proves the validator came from a scope.
/// </summary>
public sealed class ScopedDependency
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}

/// <summary>Records which <see cref="ScopedDependency"/> instance the validator saw.</summary>
public sealed class ScopedMessageValidator : AbstractValidator<ScopedMessage>
{
    public ScopedMessageValidator(ScopedDependency dependency, ScopeLog log)
    {
        log.ValidatorInstances.Enqueue(dependency.InstanceId);
        RuleFor(x => x.Value, "Value", p => p.NotEmpty());
    }
}

public sealed class FirstFieldValidator : AbstractValidator<MultiValidatedMessage>
{
    public FirstFieldValidator()
    {
        RuleFor(x => x.First, "First", p => p.NotEmpty());
    }
}

public sealed class SecondFieldValidator : AbstractValidator<MultiValidatedMessage>
{
    public SecondFieldValidator()
    {
        RuleFor(x => x.Second, "Second", p => p.NotEmpty());
    }
}

public sealed class ShipOrderValidator : AbstractValidator<IShipOrder>
{
    public ShipOrderValidator()
    {
        RuleFor(x => x.OrderId, "OrderId", p => p.NotEmpty());
    }
}

/// <summary>Counts validator activations, one per validated message.</summary>
public sealed class InvocationCounter
{
    private int count;

    public int Count => Volatile.Read(ref count);

    public void Increment() => Interlocked.Increment(ref count);
}

/// <summary>Collects the scoped-dependency instance ids seen by the validator and by the consumer.</summary>
public sealed class ScopeLog
{
    public ConcurrentQueue<Guid> ValidatorInstances { get; } = new();

    public ConcurrentQueue<Guid> ConsumerInstances { get; } = new();
}

public sealed class SubmitOrderConsumer : IConsumer<SubmitOrder>
{
    public Task Consume(ConsumeContext<SubmitOrder> context) => Task.CompletedTask;
}

public sealed class UnvalidatedMessageConsumer : IConsumer<UnvalidatedMessage>
{
    public Task Consume(ConsumeContext<UnvalidatedMessage> context) => Task.CompletedTask;
}

public sealed class AsyncOnlyMessageConsumer : IConsumer<AsyncOnlyMessage>
{
    public Task Consume(ConsumeContext<AsyncOnlyMessage> context) => Task.CompletedTask;
}

public sealed class MultiValidatedMessageConsumer : IConsumer<MultiValidatedMessage>
{
    public Task Consume(ConsumeContext<MultiValidatedMessage> context) => Task.CompletedTask;
}

public sealed class ShipOrderConsumer : IConsumer<IShipOrder>
{
    public Task Consume(ConsumeContext<IShipOrder> context) => Task.CompletedTask;
}

/// <summary>Records the <see cref="ScopedDependency"/> instance of the consume scope it runs in.</summary>
public sealed class ScopedMessageConsumer : IConsumer<ScopedMessage>
{
    private readonly ScopedDependency dependency;
    private readonly ScopeLog log;

    public ScopedMessageConsumer(ScopedDependency dependency, ScopeLog log)
    {
        this.dependency = dependency;
        this.log = log;
    }

    public Task Consume(ConsumeContext<ScopedMessage> context)
    {
        log.ConsumerInstances.Enqueue(dependency.InstanceId);
        return Task.CompletedTask;
    }
}
