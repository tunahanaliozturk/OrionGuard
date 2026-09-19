using Moongazing.OrionGuard.Utilities;

namespace Moongazing.OrionGuard.Tests;

public class LdapEncodingTests
{
    #region EscapeFilterValue (RFC 4515)

    // The examples of RFC 4515 section 4, as the value before it is escaped and after.
    [Theory]
    [InlineData("Parens R Us (for all your parenthetical needs)", "Parens R Us \\28for all your parenthetical needs\\29")]
    [InlineData("*", "\\2a")]
    [InlineData("C:\\MyFile", "C:\\5cMyFile")]
    [InlineData("Lu\u010di\u0107", "Lu\u010di\u0107")]
    public void EscapeFilterValue_ShouldEscapeTheCharactersRfc4515Requires(string value, string expected)
    {
        Assert.Equal(expected, LdapEncoding.EscapeFilterValue(value));
    }

    [Fact]
    public void EscapeFilterValue_ShouldEscapeNul()
    {
        Assert.Equal("\\00\\00\\00\u0004", LdapEncoding.EscapeFilterValue("\0\0\0\u0004"));
    }

    [Fact]
    public void EscapeFilterValue_ShouldNeutralizeAFilterThatWouldMatchEveryEntry()
    {
        // (uid=*)(|(uid=* would close the filter and add an always-true one.
        var filter = $"(uid={LdapEncoding.EscapeFilterValue("*)(|(uid=*")})";

        Assert.Equal("(uid=\\2a\\29\\28|\\28uid=\\2a)", filter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("john.doe")]
    [InlineData("admin-user")]
    public void EscapeFilterValue_ShouldReturnTheValueUnchanged_WhenNothingNeedsEscaping(string value)
    {
        Assert.Equal(value, LdapEncoding.EscapeFilterValue(value));
    }

    [Fact]
    public void EscapeFilterValue_ShouldThrow_WhenValueIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => LdapEncoding.EscapeFilterValue(null!));
    }

    #endregion

    #region EscapeDistinguishedNameValue (RFC 4514)

    // The examples of RFC 4514 section 4, plus the separators a value could otherwise inject.
    [Theory]
    [InlineData("James \"Jim\" Smith, III", "James \\\"Jim\\\" Smith\\, III")]
    [InlineData("jdoe,ou=Admins", "jdoe\\,ou=Admins")]
    [InlineData("Sales+CN=J. Smith", "Sales\\+CN=J. Smith")]
    [InlineData("a;b", "a\\;b")]
    [InlineData("a<b>c", "a\\<b\\>c")]
    [InlineData("back\\slash", "back\\\\slash")]
    [InlineData(" leading", "\\ leading")]
    [InlineData("#leading", "\\#leading")]
    [InlineData("trailing ", "trailing\\ ")]
    [InlineData(" ", "\\ ")]
    [InlineData("Lu\u010di\u0107", "Lu\u010di\u0107")]
    [InlineData("", "")]
    public void EscapeDistinguishedNameValue_ShouldEscapeTheCharactersRfc4514Requires(string value, string expected)
    {
        Assert.Equal(expected, LdapEncoding.EscapeDistinguishedNameValue(value));
    }

    [Fact]
    public void EscapeDistinguishedNameValue_ShouldEscapeNul()
    {
        Assert.Equal("a\\00b", LdapEncoding.EscapeDistinguishedNameValue("a\0b"));
    }

    [Fact]
    public void EscapeDistinguishedNameValue_ShouldKeepAnInjectedRdnInsideTheValue()
    {
        var dn = $"CN={LdapEncoding.EscapeDistinguishedNameValue("jdoe,ou=Admins")},OU=People,DC=example,DC=net";

        Assert.Equal("CN=jdoe\\,ou=Admins,OU=People,DC=example,DC=net", dn);
    }

    [Fact]
    public void EscapeDistinguishedNameValue_ShouldThrow_WhenValueIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => LdapEncoding.EscapeDistinguishedNameValue(null!));
    }

    #endregion
}
