using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

public class InternationalGuardsTests
{
    #region AgainstInvalidSwiftCode

    [Theory]
    [InlineData("DEUTDEFF")]
    [InlineData("BOFAUS3NXXX")]
    [InlineData("COBADEFF")]
    [InlineData("BNPAFRPP")]
    public void AgainstInvalidSwiftCode_ShouldNotThrow_WhenValid(string code)
    {
        var exception = Record.Exception(() => code.AgainstInvalidSwiftCode("swift"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DEUT")]
    [InlineData("12345678")]
    [InlineData("DEUTDEFFXXXX")]
    [InlineData("DE")]
    public void AgainstInvalidSwiftCode_ShouldThrow_WhenInvalid(string code)
    {
        Assert.Throws<ArgumentException>(() => code.AgainstInvalidSwiftCode("swift"));
    }

    #endregion

    #region AgainstInvalidIsbn

    [Theory]
    [InlineData("0306406152")]
    [InlineData("080442957X")]
    public void AgainstInvalidIsbn_ShouldNotThrow_WhenValidIsbn10(string isbn)
    {
        var exception = Record.Exception(() => isbn.AgainstInvalidIsbn("isbn"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("9780306406157")]
    [InlineData("9780132350884")]
    public void AgainstInvalidIsbn_ShouldNotThrow_WhenValidIsbn13(string isbn)
    {
        var exception = Record.Exception(() => isbn.AgainstInvalidIsbn("isbn"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("978-0-306-40615-7")]
    public void AgainstInvalidIsbn_ShouldNotThrow_WhenValidIsbn13WithHyphens(string isbn)
    {
        var exception = Record.Exception(() => isbn.AgainstInvalidIsbn("isbn"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234567890123456")]
    [InlineData("0306406153")]
    [InlineData("9780000000000")]
    public void AgainstInvalidIsbn_ShouldThrow_WhenInvalid(string isbn)
    {
        Assert.Throws<ArgumentException>(() => isbn.AgainstInvalidIsbn("isbn"));
    }

    #endregion

    #region AgainstInvalidVin

    [Theory]
    [InlineData("1HGBH41JXMN109186")]
    [InlineData("5YJSA1DG9DFP14705")]
    [InlineData("WBA3A5G59DNP26082")]
    public void AgainstInvalidVin_ShouldNotThrow_WhenValid(string vin)
    {
        var exception = Record.Exception(() => vin.AgainstInvalidVin("vin"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("1HGBH41JXMN10918")]
    [InlineData("1HGBH41IXMN109186")]
    [InlineData("1HGBH41OXMN109186")]
    [InlineData("1HGBH41QXMN109186")]
    public void AgainstInvalidVin_ShouldThrow_WhenInvalid(string vin)
    {
        Assert.Throws<ArgumentException>(() => vin.AgainstInvalidVin("vin"));
    }

    #endregion

    #region AgainstInvalidEan

    [Theory]
    [InlineData("4006381333931")]
    [InlineData("5901234123457")]
    public void AgainstInvalidEan_ShouldNotThrow_WhenValid(string ean)
    {
        var exception = Record.Exception(() => ean.AgainstInvalidEan("ean"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789012")]
    [InlineData("12345678901234")]
    [InlineData("4006381333932")]
    [InlineData("abcdefghijklm")]
    public void AgainstInvalidEan_ShouldThrow_WhenInvalid(string ean)
    {
        Assert.Throws<ArgumentException>(() => ean.AgainstInvalidEan("ean"));
    }

    #endregion

    #region AgainstInvalidVatNumber

    [Theory]
    [InlineData("DE123456789")]
    [InlineData("GB999999999")]
    [InlineData("FR12345678901")]
    [InlineData("NL123456789B01")]
    public void AgainstInvalidVatNumber_ShouldNotThrow_WhenValid(string vat)
    {
        var exception = Record.Exception(() => vat.AgainstInvalidVatNumber("vat"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("D1234")]
    [InlineData("XX")]
    public void AgainstInvalidVatNumber_ShouldThrow_WhenInvalid(string vat)
    {
        Assert.Throws<ArgumentException>(() => vat.AgainstInvalidVatNumber("vat"));
    }

    #endregion

    #region AgainstInvalidImei

    [Theory]
    [InlineData("490154203237518")]
    [InlineData("353879234252633")]
    public void AgainstInvalidImei_ShouldNotThrow_WhenValid(string imei)
    {
        var exception = Record.Exception(() => imei.AgainstInvalidImei("imei"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("123456789012345")]
    [InlineData("49015420323751A")]
    [InlineData("1234567890123456")]
    public void AgainstInvalidImei_ShouldThrow_WhenInvalid(string imei)
    {
        Assert.Throws<ArgumentException>(() => imei.AgainstInvalidImei("imei"));
    }

    #endregion
}
