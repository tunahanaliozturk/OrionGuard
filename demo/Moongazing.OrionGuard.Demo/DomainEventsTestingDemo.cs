using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Demonstrates the framework-agnostic <see cref="DomainEventCapture"/> and
/// <see cref="InMemoryDomainEventDispatcher"/> helpers from the <c>OrionGuard.Testing</c> package.
/// Throws <see cref="DomainEventAssertionException"/> on failure, so it works with any test runner.
/// </summary>
public static class DomainEventsTestingDemo
{
    public sealed record InventoryReserved(string Sku, int Quantity) : DomainEventBase;

    public static async Task RunAsync()
    {
        Console.WriteLine("\n== Testing helpers demo ==");

        var aggregate = new DomainEventsDemo.Order(Guid.NewGuid());
        aggregate.Ship();
        var capture = DomainEventCapture.From(aggregate);
        capture.Should().HaveRaised<DomainEventsDemo.OrderShipped>(e => e.OrderId == aggregate.Id);
        Console.WriteLine("  DomainEventCapture: assertion passed (OrderShipped raised)");

        var dispatcher = new InMemoryDomainEventDispatcher();
        await dispatcher.DispatchAsync(new InventoryReserved("SKU-001", 3));
        await dispatcher.DispatchAsync(new InventoryReserved("SKU-002", 7));

        dispatcher.Should().HaveRaisedExactly(2).Of<InventoryReserved>();
        Console.WriteLine($"  InMemoryDomainEventDispatcher: captured {dispatcher.Captured.Count} event(s)");

        try
        {
            capture.Should().HaveRaised<InventoryReserved>();
        }
        catch (DomainEventAssertionException ex)
        {
            Console.WriteLine($"  assertion failure (expected): {ex.Message}");
        }
    }
}
