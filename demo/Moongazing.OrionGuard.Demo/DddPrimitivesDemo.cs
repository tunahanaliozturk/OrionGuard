using Moongazing.OrionGuard.Demo.Domain;
using Moongazing.OrionGuard.Domain.Exceptions;
using Moongazing.OrionGuard.Exceptions;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Shows the DDD primitives: hybrid Value Objects (class + record),
/// <c>Entity&lt;TId&gt;</c> identity equality, and <c>AggregateRoot</c>
/// with synchronous and asynchronous business rules plus the domain-event
/// buffer.
/// </summary>
public static class DddPrimitivesDemo
{
    public static async Task RunAsync()
    {
        Console.WriteLine("\n== Value Objects ==");

        // Behaviour-rich VO via the abstract ValueObject base class.
        var price1 = new Money(100m, "USD");
        var price2 = new Money(100m, "USD");
        var price3 = new Money(100m, "EUR");

        Console.WriteLine($"  Class-based equality: 100 USD == 100 USD ? {price1 == price2}");
        Console.WriteLine($"  Currency matters: 100 USD != 100 EUR ? {price1 != price3}");
        Console.WriteLine($"  Hash codes match for equals: {price1.GetHashCode() == price2.GetHashCode()}");

        var doubled = price1.Add(price2);
        Console.WriteLine($"  Money.Add: 100 USD + 100 USD = {doubled}");

        // Pure-data VO via IValueObject marker on a record.
        var home = new Address("Bagdat Cd. 100", "Istanbul", "34728", "TR");
        var sameHome = new Address("Bagdat Cd. 100", "Istanbul", "34728", "TR");
        Console.WriteLine($"  Record-based VO structural equality: {home == sameHome}");

        try
        {
            _ = new Address("", "", "", "");
        }
        catch (GuardException ex)
        {
            Console.WriteLine($"  Address invariant rejected ({ex.GetType().Name}): {ex.Message}");
        }

        Console.WriteLine("\n== Entity<TId> ==");

        var customerId = CustomerId.New();
        var customer = new Customer(customerId, "alice@example.com", home);
        var sameCustomer = new Customer(customerId, "alice+newsletter@example.com", home);

        Console.WriteLine($"  Customer equality by Id only: {customer == sameCustomer}");
        Console.WriteLine("  Email differs but identity is the same, so the entities compare equal");

        customer.ChangeEmail("alice.updated@example.com");
        Console.WriteLine($"  Customer email updated to: {customer.Email}");

        Console.WriteLine("\n== AggregateRoot + Business Rules ==");

        var orderId = OrderId.New();
        var order = new Order(orderId, customerId);
        order.AddItem(new Money(49.99m, "USD"));
        order.AddItem(new Money(19.99m, "USD"));

        Console.WriteLine($"  Order created with total = {order.Total}");

        // Synchronous rule: OrderMustHaveItemsRule via Order.Place().
        order.Place();
        Console.WriteLine($"  Order.Place() succeeded (status={order.Status})");

        // Rule failure path: cannot ship an unpaid order.
        var unpaidOrder = new Order(OrderId.New(), customerId);
        unpaidOrder.AddItem(new Money(9.99m, "USD"));
        try
        {
            unpaidOrder.Ship();
        }
        catch (BusinessRuleValidationException ex)
        {
            Console.WriteLine($"  Ship blocked by rule {ex.RuleName}: {ex.Message}");
        }

        // Happy path: ship the paid order, which raises OrderShippedEvent.
        order.Ship();
        Console.WriteLine($"  Order.Ship() succeeded (status={order.Status})");

        var events = order.PullDomainEvents();
        Console.WriteLine($"  Pulled {events.Count} domain event(s) from the aggregate:");
        foreach (var domainEvent in events)
        {
            Console.WriteLine($"    {domainEvent.GetType().Name} @ {domainEvent.OccurredOnUtc:HH:mm:ss} (EventId={domainEvent.EventId})");
        }
        Console.WriteLine($"  After Pull, aggregate's DomainEvents is empty: {order.DomainEvents.Count == 0}");

        // Async rule path.
        var uniqueRule = new CustomerEmailMustBeUniqueRule(
            "alice@example.com",
            existsInStore: async email =>
            {
                await Task.Delay(5);
                return email == "alice@example.com";
            });

        try
        {
            if (await uniqueRule.IsBrokenAsync())
                throw new BusinessRuleValidationException(uniqueRule);
        }
        catch (BusinessRuleValidationException ex)
        {
            Console.WriteLine($"  Async rule broken: {ex.RuleName} - {ex.Message}");
        }
    }
}
