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
}
