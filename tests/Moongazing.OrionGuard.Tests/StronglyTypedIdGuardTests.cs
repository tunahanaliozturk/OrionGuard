using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Extensions;
using Xunit;

namespace Moongazing.OrionGuard.Tests;

public class StronglyTypedIdGuardTests
{
    private sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value);
    private sealed record IntId(int Value) : StronglyTypedId<int>(Value);
    private sealed record StringId(string Value) : StronglyTypedId<string>(Value);

    [Fact]
    public void DefaultStronglyTypedId_ShouldThrow_WhenIdIsNull()
    {
        OrderId? id = null;
        Assert.Throws<NullValueException>(() => id!.AgainstDefaultStronglyTypedId(nameof(id)));
    }

    [Fact]
    public void DefaultStronglyTypedId_ShouldThrow_WhenGuidValueIsEmpty()
    {
        var id = new OrderId(Guid.Empty);
        Assert.Throws<ZeroValueException>(() => id.AgainstDefaultStronglyTypedId(nameof(id)));
    }

    [Fact]
    public void DefaultStronglyTypedId_ShouldNotThrow_WhenGuidValueIsNotEmpty()
    {
        var id = new OrderId(Guid.NewGuid());
        var ex = Record.Exception(() => id.AgainstDefaultStronglyTypedId(nameof(id)));
        Assert.Null(ex);
    }

    [Fact]
    public void DefaultStronglyTypedId_ShouldThrow_WhenIntValueIsZero()
    {
        var id = new IntId(0);
        Assert.Throws<ZeroValueException>(() => id.AgainstDefaultStronglyTypedId(nameof(id)));
    }

    [Fact]
    public void DefaultStronglyTypedId_ShouldThrow_WhenStringValueIsNullOrEmpty()
    {
        var id = new StringId(string.Empty);
        Assert.Throws<ZeroValueException>(() => id.AgainstDefaultStronglyTypedId(nameof(id)));
    }

    [Fact]
    public void DefaultStronglyTypedId_ShouldReturnInstance_ForChaining()
    {
        var id = new IntId(7);
        var returned = id.AgainstDefaultStronglyTypedId(nameof(id));
        Assert.Same(id, returned);
    }
}
