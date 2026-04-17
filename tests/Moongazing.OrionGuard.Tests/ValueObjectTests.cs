using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class ValueObjectTests
{
    private sealed record RecordAddress(string Street, string City) : IValueObject;

    [Fact]
    public void IValueObject_ShouldBeImplementableByRecord_WhenMarkerAppliedToRecord()
    {
        IValueObject vo = new RecordAddress("Main St", "Ankara");

        Assert.NotNull(vo);
        Assert.IsAssignableFrom<IValueObject>(vo);
    }
}
