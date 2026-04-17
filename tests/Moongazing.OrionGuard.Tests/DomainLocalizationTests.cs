using System.Globalization;
using Moongazing.OrionGuard.Localization;

namespace Moongazing.OrionGuard.Tests;

public class DomainLocalizationTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("tr")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("ar")]
    [InlineData("ja")]
    [InlineData("it")]
    [InlineData("zh")]
    [InlineData("ko")]
    [InlineData("ru")]
    [InlineData("nl")]
    [InlineData("pl")]
    public void ValidationMessages_ShouldResolveDomainKeys_ForEveryBundledLanguage(string culture)
    {
        var ci = new CultureInfo(culture);

        var defaultIdMsg = ValidationMessages.Get("DefaultStronglyTypedId", ci, "OrderId");
        var brokenRuleMsg = ValidationMessages.Get("BusinessRuleBroken", ci, "SomeRule");
        var invariantMsg = ValidationMessages.Get("DomainInvariantViolated", ci, "SomeInvariant");

        Assert.NotEqual("DefaultStronglyTypedId", defaultIdMsg);
        Assert.NotEqual("BusinessRuleBroken", brokenRuleMsg);
        Assert.NotEqual("DomainInvariantViolated", invariantMsg);

        Assert.Contains("OrderId", defaultIdMsg);
        Assert.Contains("SomeRule", brokenRuleMsg);
        Assert.Contains("SomeInvariant", invariantMsg);
    }
}
