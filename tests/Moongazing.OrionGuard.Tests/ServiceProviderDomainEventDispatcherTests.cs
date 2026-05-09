using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Tests;

public class ServiceProviderDomainEventDispatcherTests
{
    private sealed record TestEvent(string Payload) : DomainEventBase;

    private sealed class CountingHandler : IDomainEventHandler<TestEvent>
    {
        public int Calls { get; private set; }
        public List<string> Payloads { get; } = new();
        public Task HandleAsync(TestEvent @event, CancellationToken ct)
        {
            Calls++;
            Payloads.Add(@event.Payload);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    private static (IServiceProvider sp, T handler) BuildSp<T>(DispatchMode mode = DispatchMode.SequentialFailFast)
        where T : class, IDomainEventHandler<TestEvent>, new()
    {
        var handler = new T();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler);
        services.AddSingleton(new DomainEventDispatchOptions { Mode = mode });
        services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        return (services.BuildServiceProvider(), handler);
    }

    [Fact]
    public async Task DispatchAsync_InvokesHandler_WhenEventDispatched()
    {
        var (sp, handler) = BuildSp<CountingHandler>();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new TestEvent("a"));

        Assert.Equal(1, handler.Calls);
        Assert.Equal("a", handler.Payloads.Single());
    }

    [Fact]
    public async Task DispatchAsync_BatchOverload_InvokesHandlerForEachEvent()
    {
        var (sp, handler) = BuildSp<CountingHandler>();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new IDomainEvent[] { new TestEvent("a"), new TestEvent("b") });

        Assert.Equal(2, handler.Calls);
        Assert.Equal(new[] { "a", "b" }, handler.Payloads);
    }

    [Fact]
    public async Task DispatchAsync_FailFast_PropagatesFirstException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>, ThrowingHandler>();
        services.AddSingleton<IDomainEventHandler<TestEvent>, CountingHandler>();
        services.AddSingleton(new DomainEventDispatchOptions { Mode = DispatchMode.SequentialFailFast });
        services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new TestEvent("a")));
    }

    [Fact]
    public async Task DispatchAsync_ContinueOnError_RunsAllHandlersAndAggregatesExceptions()
    {
        var counting = new CountingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>, ThrowingHandler>();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(counting);
        services.AddSingleton(new DomainEventDispatchOptions { Mode = DispatchMode.SequentialContinueOnError });
        services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => dispatcher.DispatchAsync(new TestEvent("a")));

        Assert.Single(ex.InnerExceptions);
        Assert.IsType<InvalidOperationException>(ex.InnerExceptions[0]);
        Assert.Equal(1, counting.Calls);
    }

    [Fact]
    public async Task DispatchAsync_Parallel_InvokesAllHandlers()
    {
        var h1 = new CountingHandler();
        var h2 = new CountingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(h1);
        services.AddSingleton<IDomainEventHandler<TestEvent>>(h2);
        services.AddSingleton(new DomainEventDispatchOptions { Mode = DispatchMode.Parallel });
        services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new TestEvent("a"));

        Assert.Equal(1, h1.Calls);
        Assert.Equal(1, h2.Calls);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlerRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DomainEventDispatchOptions());
        services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<IDomainEventDispatcher>().DispatchAsync(new TestEvent("a"));
    }

    [Fact]
    public async Task DispatchAsync_NullEvent_ThrowsArgumentNullException()
    {
        var (sp, _) = BuildSp<CountingHandler>();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.DispatchAsync((IDomainEvent)null!));
    }
}
