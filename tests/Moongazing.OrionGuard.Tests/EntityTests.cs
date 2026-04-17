using System.Threading;
using System.Threading.Tasks;
using Moongazing.OrionGuard.Domain.Exceptions;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Domain.Rules;

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

    private sealed class AlwaysBrokenRule : IBusinessRule
    {
        public bool IsBroken() => true;
        public string MessageKey => "TestBrokenRule";
        public string DefaultMessage => "Rule intentionally broken for test.";
    }

    private sealed class NeverBrokenRule : IBusinessRule
    {
        public bool IsBroken() => false;
        public string MessageKey => "TestOkRule";
        public string DefaultMessage => "Never thrown.";
    }

    private sealed class AlwaysBrokenAsyncRule : IAsyncBusinessRule
    {
        public Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public string MessageKey => "TestAsyncBrokenRule";
        public string DefaultMessage => "Async rule broken.";
    }

    private sealed class CustomerWithRules : Entity<int>
    {
        public CustomerWithRules(int id) : base(id) { }

        public void EnforceSync(IBusinessRule rule) => CheckRule(rule);

        public Task EnforceAsync(IAsyncBusinessRule rule, CancellationToken ct = default)
            => CheckRuleAsync(rule, ct);
    }

    [Fact]
    public void CheckRule_ShouldThrowBusinessRuleValidationException_WhenRuleIsBroken()
    {
        var entity = new CustomerWithRules(1);

        var ex = Assert.Throws<BusinessRuleValidationException>(() => entity.EnforceSync(new AlwaysBrokenRule()));
        Assert.Equal("TestBrokenRule", ex.MessageKey);
        Assert.Equal(nameof(AlwaysBrokenRule), ex.RuleName);
        Assert.Equal("Rule intentionally broken for test.", ex.Message);
    }

    [Fact]
    public void CheckRule_ShouldNotThrow_WhenRuleIsNotBroken()
    {
        var entity = new CustomerWithRules(1);

        var ex = Record.Exception(() => entity.EnforceSync(new NeverBrokenRule()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task CheckRuleAsync_ShouldThrowBusinessRuleValidationException_WhenAsyncRuleIsBroken()
    {
        var entity = new CustomerWithRules(1);

        await Assert.ThrowsAsync<BusinessRuleValidationException>(
            () => entity.EnforceAsync(new AlwaysBrokenAsyncRule()));
    }
}
