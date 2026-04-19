using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

public class NestedValidatorTests
{
    #region Test DTOs

    private sealed class Order
    {
        public string? OrderNumber { get; set; }
        public Address? Address { get; set; }
        public List<OrderItem>? Items { get; set; }
        public decimal TotalAmount { get; set; }
    }

    private sealed class Address
    {
        public string? City { get; set; }
        public string? ZipCode { get; set; }
        public Country? Country { get; set; }
    }

    private sealed class Country
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
    }

    private sealed class OrderItem
    {
        public string? ProductName { get; set; }
        public int Quantity { get; set; }
    }

    #endregion

    #region Simple Property Validation

    [Fact]
    public void Property_ShouldPass_WhenValid()
    {
        var order = new Order { OrderNumber = "ORD-001" };

        var result = Validate.Nested(order)
            .Property(o => o.OrderNumber, p => p.NotEmpty())
            .ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Property_ShouldFail_WhenEmpty()
    {
        var order = new Order { OrderNumber = "" };

        var result = Validate.Nested(order)
            .Property(o => o.OrderNumber, p => p.NotEmpty())
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Single(result.Errors);
        Assert.Equal("OrderNumber", result.Errors[0].ParameterName);
    }

    [Fact]
    public void Property_ShouldFail_WhenNull()
    {
        var order = new Order { OrderNumber = null };

        var result = Validate.Nested(order)
            .Property(o => o.OrderNumber, p => p.NotNull())
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal("NOT_NULL", result.Errors[0].ErrorCode);
    }

    #endregion

    #region Nested Object Validation (Depth 2)

    [Fact]
    public void Nested_ShouldValidateChildProperties()
    {
        var order = new Order
        {
            OrderNumber = "ORD-001",
            Address = new Address { City = "Istanbul", ZipCode = "34000" }
        };

        var result = Validate.Nested(order)
            .Nested(o => o.Address, address => address
                .Property(a => a.City, p => p.NotEmpty())
                .Property(a => a.ZipCode, p => p.NotEmpty()))
            .ToResult();

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Nested_ShouldIncludeFullDotNotationPath()
    {
        var order = new Order
        {
            OrderNumber = "ORD-001",
            Address = new Address { City = "", ZipCode = "34000" }
        };

        var result = Validate.Nested(order)
            .Nested(o => o.Address, address => address
                .Property(a => a.City, p => p.NotEmpty()))
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal("Address.City", result.Errors[0].ParameterName);
    }

    [Fact]
    public void Nested_Depth3_ShouldBuildCorrectPath()
    {
        var order = new Order
        {
            Address = new Address
            {
                City = "Istanbul",
                Country = new Country { Name = "", Code = "TR" }
            }
        };

        var result = Validate.Nested(order)
            .Nested(o => o.Address, address => address
                .Nested(a => a.Country, country => country
                    .Property(c => c.Name, p => p.NotEmpty())))
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal("Address.Country.Name", result.Errors[0].ParameterName);
    }

    #endregion

    #region Null Nested Object Handling

    [Fact]
    public void Nested_ShouldAddError_WhenNestedObjectIsNull()
    {
        var order = new Order { OrderNumber = "ORD-001", Address = null };

        var result = Validate.Nested(order)
            .Nested(o => o.Address, address => address
                .Property(a => a.City, p => p.NotEmpty()))
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal("Address", result.Errors[0].ParameterName);
        Assert.Equal("NOT_NULL", result.Errors[0].ErrorCode);
    }

    #endregion

    #region Collection Validation with Indexed Paths

    [Fact]
    public void Collection_ShouldValidateEachItem()
    {
        var order = new Order
        {
            Items = new List<OrderItem>
            {
                new() { ProductName = "Widget", Quantity = 1 },
                new() { ProductName = "", Quantity = 0 }
            }
        };

        var result = Validate.Nested(order)
            .Collection(o => o.Items, (item, index) => item
                .Property(i => i.ProductName, p => p.NotEmpty())
                .Property(i => i.Quantity, p => p.GreaterThan(0)))
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Collection_ShouldIncludeIndexedPaths()
    {
        var order = new Order
        {
            Items = new List<OrderItem>
            {
                new() { ProductName = "Widget", Quantity = 1 },
                new() { ProductName = "", Quantity = 1 }
            }
        };

        var result = Validate.Nested(order)
            .Collection(o => o.Items, (item, index) => item
                .Property(i => i.ProductName, p => p.NotEmpty()))
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Single(result.Errors);
        Assert.Equal("Items[1].ProductName", result.Errors[0].ParameterName);
    }

    [Fact]
    public void Collection_ShouldAddError_WhenCollectionIsNull()
    {
        var order = new Order { Items = null };

        var result = Validate.Nested(order)
            .Collection(o => o.Items, (item, index) => item
                .Property(i => i.ProductName, p => p.NotEmpty()))
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal("Items", result.Errors[0].ParameterName);
        Assert.Equal("NOT_NULL", result.Errors[0].ErrorCode);
    }

    #endregion

    #region When Conditional

    [Fact]
    public void When_ShouldApplyValidation_WhenConditionTrue()
    {
        var order = new Order { TotalAmount = 1000, OrderNumber = null };

        var result = Validate.Nested(order)
            .When(o => o.TotalAmount > 500, v => v
                .Property(o => o.OrderNumber, p => p.NotNull()))
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void When_ShouldSkipValidation_WhenConditionFalse()
    {
        var order = new Order { TotalAmount = 100, OrderNumber = null };

        var result = Validate.Nested(order)
            .When(o => o.TotalAmount > 500, v => v
                .Property(o => o.OrderNumber, p => p.NotNull()))
            .ToResult();

        Assert.True(result.IsValid);
    }

    #endregion

    #region Must Custom Predicate

    [Fact]
    public void Must_ShouldFail_WhenPredicateReturnsFalse()
    {
        var order = new Order { TotalAmount = -10 };

        var result = Validate.Nested(order)
            .Must(o => o.TotalAmount >= 0, "Total amount cannot be negative")
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Contains("Total amount cannot be negative", result.Errors[0].Message);
    }

    [Fact]
    public void Must_ShouldPass_WhenPredicateReturnsTrue()
    {
        var order = new Order { TotalAmount = 50 };

        var result = Validate.Nested(order)
            .Must(o => o.TotalAmount >= 0, "Total amount cannot be negative")
            .ToResult();

        Assert.True(result.IsValid);
    }

    #endregion

    #region ThrowIfInvalid

    [Fact]
    public void ThrowIfInvalid_ShouldThrow_WhenErrors()
    {
        var order = new Order { OrderNumber = null };

        Assert.Throws<AggregateValidationException>(() =>
            Validate.Nested(order)
                .Property(o => o.OrderNumber, p => p.NotNull())
                .ThrowIfInvalid());
    }

    [Fact]
    public void ThrowIfInvalid_ShouldReturnInstance_WhenValid()
    {
        var order = new Order { OrderNumber = "ORD-001" };

        var returned = Validate.Nested(order)
            .Property(o => o.OrderNumber, p => p.NotEmpty())
            .ThrowIfInvalid();

        Assert.Same(order, returned);
    }

    #endregion
}
