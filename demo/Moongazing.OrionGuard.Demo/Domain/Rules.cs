using Moongazing.OrionGuard.Domain.Rules;

namespace Moongazing.OrionGuard.Demo.Domain;

// Synchronous business rule — evaluated inline with no I/O.
public sealed class OrderMustBePaidRule(Order order) : IBusinessRule
{
    public bool IsBroken() => order.Status != OrderStatus.Paid;
    public string MessageKey => nameof(OrderMustBePaidRule);
    public string DefaultMessage => "Order must be paid before shipping.";
}

public sealed class OrderMustHaveItemsRule(Order order) : IBusinessRule
{
    public bool IsBroken() => order.LineItemCount == 0;
    public string MessageKey => nameof(OrderMustHaveItemsRule);
    public string DefaultMessage => "Order must have at least one line item.";
}

// Asynchronous business rule — useful when rule evaluation needs I/O
// (e.g., uniqueness check against a repository).
public sealed class CustomerEmailMustBeUniqueRule(string email, Func<string, Task<bool>> existsInStore) : IAsyncBusinessRule
{
    public async Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default)
        => await existsInStore(email).ConfigureAwait(false);

    public string MessageKey => nameof(CustomerEmailMustBeUniqueRule);
    public string DefaultMessage => "Customer e-mail must be unique.";
    public object[]? MessageArgs => new object[] { email };
}
