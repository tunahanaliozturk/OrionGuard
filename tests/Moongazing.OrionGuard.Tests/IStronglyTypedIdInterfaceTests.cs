using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class IStronglyTypedIdInterfaceTests
{
    private sealed record InvoiceId(Guid Value) : StronglyTypedId<Guid>(Value);

    [Fact]
    public void StronglyTypedId_ShouldBeAssignableToIStronglyTypedId_WhenConstructed()
    {
        var id = new InvoiceId(Guid.NewGuid());

        IStronglyTypedId<Guid> asInterface = id;

        Assert.NotNull(asInterface);
        Assert.Equal(id.Value, asInterface.Value);
    }
}
