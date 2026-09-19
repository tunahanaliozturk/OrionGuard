using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

public class AdvancedStringGuardsTests
{
    // 314 bytes that expand to 9 million characters when the DTD is processed.
    private const string BillionLaughs =
        "<?xml version=\"1.0\"?><!DOCTYPE l [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">" +
        "<!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\"><!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\">" +
        "<!ENTITY e \"&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;\"><!ENTITY f \"&e;&e;&e;&e;&e;&e;&e;&e;&e;&e;\">]>" +
        "<l>&f;&f;&f;&f;&f;&f;&f;&f;&f;</l>";

    private const string ExternalEntity =
        "<?xml version=\"1.0\"?><!DOCTYPE r [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><r>&x;</r>";

    #region AgainstInvalidXml

    [Theory]
    [InlineData(BillionLaughs)]
    [InlineData(ExternalEntity)]
    public void AgainstInvalidXml_ShouldThrow_WhenDocumentDeclaresDtd(string xml)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        Assert.Throws<ArgumentException>(() => xml.AgainstInvalidXml("xml"));

        // Prohibiting the DTD stops the parser before any entity is expanded.
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore < 10 * 1024 * 1024);
    }

    [Fact]
    public void GuardAgainstInvalidXml_ShouldThrowInvalidXmlException_WhenDocumentDeclaresDtd()
    {
        Assert.Throws<InvalidXmlException>(() => Guard.AgainstInvalidXml(ExternalEntity, "xml"));
    }

    [Theory]
    [InlineData("<root><item id=\"1\">value</item></root>")]
    [InlineData("<?xml version=\"1.0\" encoding=\"utf-8\"?><a xmlns=\"urn:x\"><b/></a>")]
    public void AgainstInvalidXml_ShouldNotThrow_WhenDocumentIsWellFormed(string xml)
    {
        Assert.Null(Record.Exception(() => xml.AgainstInvalidXml("xml")));
        Assert.Null(Record.Exception(() => Guard.AgainstInvalidXml(xml, "xml")));
    }

    [Theory]
    [InlineData("<root>")]
    [InlineData("<a/><b/>")]
    [InlineData("not xml")]
    public void AgainstInvalidXml_ShouldStillThrow_WhenDocumentIsMalformed(string xml)
    {
        Assert.Throws<ArgumentException>(() => xml.AgainstInvalidXml("xml"));
        Assert.Throws<InvalidXmlException>(() => Guard.AgainstInvalidXml(xml, "xml"));
    }

    #endregion

    #region AgainstInvalidCreditCard

    [Theory]
    [InlineData("-")]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("0000 0000 00")]
    [InlineData("00000000000000000000")]
    // Arabic-Indic digits: char.IsDigit accepts them, and c - '0' made this string pass the Luhn sum.
    [InlineData("٤١١١١١١١١١١١١١١٧")]
    public void AgainstInvalidCreditCard_ShouldThrow_WhenNotTwelveToNineteenAsciiDigits(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidCreditCard("card"));
    }

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("4111 1111 1111 1111")]
    [InlineData("3782-822463-10005")]
    public void AgainstInvalidCreditCard_ShouldNotThrow_WhenCardIsValid(string value)
    {
        Assert.Null(Record.Exception(() => value.AgainstInvalidCreditCard("card")));
    }

    #endregion

    #region AgainstInvalidMasterCard

    // Every number below passes the Luhn check, so only the range decides.
    [Theory]
    [InlineData("5555555555554444")]  // 51-55, the original range
    [InlineData("5105105105105100")]
    [InlineData("2221000000000009")]  // 2221-2720, the range Mastercard added in 2017
    [InlineData("2223003122003222")]
    [InlineData("2720000000000005")]
    [InlineData("2720 0000 0000 0005")]
    [InlineData("2720-0000-0000-0005")]
    public void AgainstInvalidMasterCard_ShouldNotThrow_WhenNumberIsInAMastercardRange(string value)
    {
        Assert.Null(Record.Exception(() => value.AgainstInvalidMasterCard("card")));
    }

    [Theory]
    [InlineData("2220000000000000")]  // just below the 2-series range
    [InlineData("2721000000000004")]  // just above it
    [InlineData("4111111111111111")]  // Visa
    [InlineData("5555555555554443")]  // right range, fails Luhn
    public void AgainstInvalidMasterCard_ShouldThrow_WhenNumberIsOutsideTheRangesOrFailsLuhn(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidMasterCard("card"));
    }

    #endregion
}
