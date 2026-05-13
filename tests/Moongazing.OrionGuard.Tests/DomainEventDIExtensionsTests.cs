using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Tests;

public class DomainEventDIExtensionsTests
{
    public sealed record SampleEvent(int Id) : DomainEventBase;

    public sealed class SampleHandler : IDomainEventHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent @event, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void AddOrionGuardDomainEvents_RegistersDispatcherAndOptions()
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var sp = services.BuildServiceProvider();

        Assert.IsType<ServiceProviderDomainEventDispatcher>(sp.GetRequiredService<IDomainEventDispatcher>());
        Assert.Equal(DispatchMode.SequentialFailFast, sp.GetRequiredService<DomainEventDispatchOptions>().Mode);
    }

    [Fact]
    public void AddOrionGuardDomainEvents_DefaultsToSequentialFailFast()
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<DomainEventDispatchOptions>();
        Assert.Equal(DispatchMode.SequentialFailFast, opts.Mode);
    }

    [Fact]
    public void AddOrionGuardDomainEventHandlers_ScansAssembly_RegistersHandlers()
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEventHandlers(typeof(DomainEventDIExtensionsTests).Assembly);
        var sp = services.BuildServiceProvider();

        var handler = sp.GetRequiredService<IDomainEventHandler<SampleEvent>>();
        Assert.IsType<SampleHandler>(handler);
    }

    [Fact]
    public void AddOrionGuardDomainEvents_ConfigureCallback_AppliesMode()
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents(o => o.Mode = DispatchMode.Parallel);
        var sp = services.BuildServiceProvider();

        Assert.Equal(DispatchMode.Parallel, sp.GetRequiredService<DomainEventDispatchOptions>().Mode);
    }

    [Fact]
    public void AddOrionGuardDomainEvents_CalledTwice_OnlyRegistersOnce()
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        services.AddOrionGuardDomainEvents();

        var optionsCount = services.Count(d => d.ServiceType == typeof(DomainEventDispatchOptions));
        var dispatcherCount = services.Count(d => d.ServiceType == typeof(IDomainEventDispatcher));

        Assert.Equal(1, optionsCount);
        Assert.Equal(1, dispatcherCount);
    }

    [Fact]
    public void AddOrionGuardDomainEventHandlers_SkipsOpenGenericHandlers()
    {
        var services = new ServiceCollection();
        // Should not throw when scanning the test assembly which contains both
        // SampleHandler (closed) and the open-generic guard test types we just added.
        var ex = Record.Exception(() =>
            services.AddOrionGuardDomainEventHandlers(typeof(DomainEventDIExtensionsTests).Assembly));

        Assert.Null(ex);
    }
}
