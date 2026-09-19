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
}
