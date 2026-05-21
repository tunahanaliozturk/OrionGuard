using Microsoft.AspNetCore.Http;
using Moongazing.OrionGuard.AspNetCore.ProblemDetails;
using Moongazing.OrionGuard.Domain.Exceptions;
using Moongazing.OrionGuard.Domain.Rules;

namespace Moongazing.OrionGuard.AspNetCore.Tests.ProblemDetails;

public class OrionGuardProblemDetailsFactory_BusinessRuleTests
{
    private sealed class BrokenRule : BusinessRule
    {
        public override bool IsBroken() => true;
        public override string DefaultMessage => "Order must have at least one item.";
    }

    [Fact]
    public void Create_ShouldUse422_AsDefaultStatus()
    {
        var pd = OrionGuardProblemDetailsFactory.Create(new BusinessRuleValidationException(new BrokenRule()));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, pd.Status);
    }

    [Fact]
    public void Create_ShouldUseRuleSpecificType()
    {
        var pd = OrionGuardProblemDetailsFactory.Create(new BusinessRuleValidationException(new BrokenRule()));
        Assert.Equal("https://moongazing.dev/orionguard/problems/business-rule-violation", pd.Type);
    }

    [Fact]
    public void Create_ShouldKeyErrorsByRuleName()
    {
        var pd = OrionGuardProblemDetailsFactory.Create(new BusinessRuleValidationException(new BrokenRule()));
        Assert.True(pd.Errors.ContainsKey(nameof(BrokenRule)));
        Assert.Equal(new[] { "Order must have at least one item." }, pd.Errors[nameof(BrokenRule)]);
    }

    [Fact]
    public void Create_ShouldUseBusinessRuleViolationTitle()
    {
        var pd = OrionGuardProblemDetailsFactory.Create(new BusinessRuleValidationException(new BrokenRule()));
        Assert.Equal("Business Rule Violation", pd.Title);
    }
}
