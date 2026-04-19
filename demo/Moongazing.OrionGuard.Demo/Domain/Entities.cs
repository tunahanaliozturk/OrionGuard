using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Demo.Domain;

public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Cancelled
}

// Entity example — identity equality via Id, other state can change over time.
public sealed class Customer : Entity<CustomerId>
{
    public string Email { get; private set; } = string.Empty;
    public Address ShippingAddress { get; private set; } = default!;

    public Customer(CustomerId id, string email, Address shippingAddress) : base(id)
    {
        id.AgainstDefaultStronglyTypedId(nameof(id));
        Ensure.That(email).NotNull().Email();
        Ensure.That(shippingAddress).NotNull();
        Email = email;
        ShippingAddress = shippingAddress;
    }

    // EF Core / serializer constructor.
    private Customer() { }

    public void ChangeEmail(string newEmail)
    {
        Ensure.That(newEmail).NotNull().Email();
        Email = newEmail;
    }
}

// Aggregate root example — owns a cluster of domain concepts, enforces invariants,
// and raises domain events. PullDomainEvents returns and clears the buffer so a
// dispatcher (e.g., the MediatR bridge in v6.2) can publish them post-commit.
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<Money> _lineItems = new();

    public CustomerId CustomerId { get; private set; } = default!;
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public Money Total => _lineItems.Count == 0
        ? new Money(0, "USD")
        : _lineItems.Aggregate((a, b) => a.Add(b));

    public int LineItemCount => _lineItems.Count;

    public Order(OrderId id, CustomerId customerId) : base(id)
    {
        id.AgainstDefaultStronglyTypedId(nameof(id));
        customerId.AgainstDefaultStronglyTypedId(nameof(customerId));
        CustomerId = customerId;
    }

    private Order() { }

    public void AddItem(Money price)
    {
        Ensure.That(price).NotNull();
        _lineItems.Add(price);
    }

    public void Place()
    {
        // CheckRule is a protected helper from Entity<TId>.
        // It throws BusinessRuleValidationException (localized via ValidationMessages)
        // when the rule reports itself as broken.
        CheckRule(new OrderMustHaveItemsRule(this));
        Status = OrderStatus.Paid; // simplification: pay inline for the demo
        RaiseEvent(new OrderPlacedEvent(Id, Total));
    }

    public void Ship()
    {
        CheckRule(new OrderMustBePaidRule(this));
        Status = OrderStatus.Shipped;
        RaiseEvent(new OrderShippedEvent(Id, DateTime.UtcNow));
    }
}
