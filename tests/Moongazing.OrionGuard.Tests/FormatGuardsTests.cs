using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

public class FormatGuardsTests
{
    #region Geographic Coordinates

    [Theory]
    [InlineData(0.0)]
    [InlineData(41.0082)]
    [InlineData(-90.0)]
    [InlineData(90.0)]
    public void AgainstInvalidLatitude_ShouldNotThrow_WhenValid(double value)
    {
        var ex = Record.Exception(() => value.AgainstInvalidLatitude("lat"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(-90.1)]
    [InlineData(90.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AgainstInvalidLatitude_ShouldThrow_WhenInvalid(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => value.AgainstInvalidLatitude("lat"));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(28.9784)]
    [InlineData(-180.0)]
    [InlineData(180.0)]
    public void AgainstInvalidLongitude_ShouldNotThrow_WhenValid(double value)
    {
        var ex = Record.Exception(() => value.AgainstInvalidLongitude("lon"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(-180.1)]
    [InlineData(180.1)]
    [InlineData(double.NaN)]
    public void AgainstInvalidLongitude_ShouldThrow_WhenInvalid(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => value.AgainstInvalidLongitude("lon"));
    }

    [Fact]
    public void AgainstInvalidCoordinates_ShouldNotThrow_WhenBothValid()
    {
        var ex = Record.Exception(() => FormatGuards.AgainstInvalidCoordinates(41.0, 29.0, "coords"));
        Assert.Null(ex);
    }

    [Fact]
    public void AgainstInvalidCoordinates_ShouldThrow_WhenLatitudeInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FormatGuards.AgainstInvalidCoordinates(91.0, 29.0, "coords"));
    }

    #endregion

    #region MAC Address

    [Theory]
    [InlineData("00:1A:2B:3C:4D:5E")]
    [InlineData("00-1A-2B-3C-4D-5E")]
    [InlineData("001A.2B3C.4D5E")]
    [InlineData("001A2B3C4D5E")]
    public void AgainstInvalidMacAddress_ShouldNotThrow_WhenValid(string value)
    {
        var ex = Record.Exception(() => value.AgainstInvalidMacAddress("mac"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("00:1A:2B:3C:4D")]
    [InlineData("00:1A:2B:3C:4D:5E:FF")]
    [InlineData("GG:1A:2B:3C:4D:5E")]
    public void AgainstInvalidMacAddress_ShouldThrow_WhenInvalid(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidMacAddress("mac"));
    }

    #endregion

    #region Hostname

    [Theory]
    [InlineData("example.com")]
    [InlineData("api.example.com")]
    [InlineData("my-host")]
    [InlineData("localhost")]
    public void AgainstInvalidHostname_ShouldNotThrow_WhenValid(string value)
    {
        var ex = Record.Exception(() => value.AgainstInvalidHostname("host"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-invalid.com")]
    [InlineData("invalid-.com")]
    [InlineData("host name.com")]
    public void AgainstInvalidHostname_ShouldThrow_WhenInvalid(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidHostname("host"));
    }

    #endregion

    #region CIDR

    [Theory]
    [InlineData("192.168.1.0/24")]
    [InlineData("10.0.0.0/8")]
    [InlineData("0.0.0.0/0")]
    [InlineData("255.255.255.255/32")]
    public void AgainstInvalidCidr_ShouldNotThrow_WhenValid(string value)
    {
        var ex = Record.Exception(() => value.AgainstInvalidCidr("cidr"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("192.168.1.0")]
    [InlineData("192.168.1.0/33")]
    [InlineData("not-an-ip/24")]
    [InlineData("192.168.1.0/-1")]
    public void AgainstInvalidCidr_ShouldThrow_WhenInvalid(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidCidr("cidr"));
    }

    #endregion

    #region ISO Country Code

    [Theory]
    [InlineData("US")]
    [InlineData("TR")]
    [InlineData("DE")]
    [InlineData("GB")]
    [InlineData("JP")]
    public void AgainstInvalidCountryCode_ShouldNotThrow_WhenValid(string value)
    {
        var ex = Record.Exception(() => value.AgainstInvalidCountryCode("country"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("XX")]
    [InlineData("USA")]
    [InlineData("1")]
    public void AgainstInvalidCountryCode_ShouldThrow_WhenInvalid(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidCountryCode("country"));
    }

    #endregion

    #region Time Zone ID

    [Fact]
    public void AgainstInvalidTimeZoneId_ShouldNotThrow_WhenValid()
    {
        // Use a system time zone that exists on all platforms
        var tz = TimeZoneInfo.Local.Id;
        var ex = Record.Exception(() => tz.AgainstInvalidTimeZoneId("tz"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/A/Real/Timezone")]
    public void AgainstInvalidTimeZoneId_ShouldThrow_WhenInvalid(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidTimeZoneId("tz"));
    }

    #endregion

    #region Language Tag

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    [InlineData("de")]
    public void AgainstInvalidLanguageTag_ShouldNotThrow_WhenValid(string value)
    {
        var ex = Record.Exception(() => value.AgainstInvalidLanguageTag("lang"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AgainstInvalidLanguageTag_ShouldThrow_WhenInvalid(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidLanguageTag("lang"));
    }

    #endregion

    #region JWT Format

    [Fact]
    public void AgainstInvalidJwtFormat_ShouldNotThrow_WhenValid()
    {
        var jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abc123";
        var ex = Record.Exception(() => jwt.AgainstInvalidJwtFormat("token"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two")]
    [InlineData("one.two.three.four")]
    public void AgainstInvalidJwtFormat_ShouldThrow_WhenInvalid(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidJwtFormat("token"));
    }

    #endregion

    #region Connection String

    [Theory]
    [InlineData("Server=localhost;Database=mydb")]
    [InlineData("Host=db.example.com;Port=5432;Database=app")]
    [InlineData("Data Source=.;Initial Catalog=TestDb;Integrated Security=True")]
    public void AgainstInvalidConnectionString_ShouldNotThrow_WhenValid(string value)
    {
        var ex = Record.Exception(() => value.AgainstInvalidConnectionString("connStr"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-equals-sign-here")]
    public void AgainstInvalidConnectionString_ShouldThrow_WhenInvalid(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidConnectionString("connStr"));
    }

    #endregion

    #region Base64 String

    [Theory]
    [InlineData("SGVsbG8=")]
    [InlineData("dGVzdA==")]
    [InlineData("YWJj")]
    public void AgainstInvalidBase64String_ShouldNotThrow_WhenValid(string value)
    {
        var ex = Record.Exception(() => value.AgainstInvalidBase64String("b64"));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void AgainstInvalidBase64String_ShouldThrow_WhenInvalid(string value)
    {
        Assert.Throws<ArgumentException>(() => value.AgainstInvalidBase64String("b64"));
    }

    #endregion
}
