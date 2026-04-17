using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class EntityTests
{
    private sealed class Customer : Entity<int>
    {
        public Customer(int id) : base(id) { }
        public Customer() { }
    }

    [Fact]
    public void Equals_ShouldReturnTrue_WhenIdsMatch()
    {
        var a = new Customer(1);
        var b = new Customer(1);

        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_ShouldReturnFalse_WhenIdsDiffer()
    {
        var a = new Customer(1);
        var b = new Customer(2);

        Assert.False(a.Equals(b));
        Assert.True(a != b);
    }

    [Fact]
    public void Ctor_ShouldThrow_WhenIdIsNullReferenceType()
    {
        Assert.Throws<ArgumentNullException>(() => new ReferenceIdEntity(null!));
    }

    [Fact]
    public void ParameterlessCtor_ShouldBeUsable_ForSerializers()
    {
        var customer = new Customer();

        Assert.Equal(0, customer.Id);
    }

    private sealed class ReferenceIdEntity : Entity<string>
    {
        public ReferenceIdEntity(string id) : base(id) { }
    }
}
