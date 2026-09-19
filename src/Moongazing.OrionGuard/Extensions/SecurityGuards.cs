using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
#if !NET9_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Heuristic checks that reject input carrying common injection payloads, plus structural guards for
/// file paths and redirect targets.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are heuristics, not a defence.</b> <see cref="AgainstSqlInjection"/>, <see cref="AgainstXss"/>,
/// <see cref="AgainstCommandInjection"/>, <see cref="AgainstLdapInjection"/>, <see cref="AgainstXxe"/> and
/// <see cref="AgainstInjection"/> are denylists. They reject input that contains a known attack token, so a
/// payload written in a form the list does not know passes, and ordinary text that happens to contain a
/// listed token is rejected. Use them to turn away obviously hostile input early and to flag it in logs.
/// The defence is always in the sink: parameterized queries for SQL, contextual output encoding for HTML,
/// attributes, JavaScript and URLs, <see cref="ProcessStartInfo.ArgumentList"/> without a shell for
/// processes, <see cref="Utilities.LdapEncoding"/> (RFC 4515 / RFC 4514) for LDAP filters and distinguished
/// names, and an XML reader with DTD processing prohibited for XML.
/// </para>
/// <para>
/// <b>Structural guards.</b> <see cref="AgainstOpenRedirect"/>, <see cref="AgainstPathTraversal"/> and
/// <see cref="AgainstPathEscape"/> decide from the shape of the value; each one's remarks state what it
/// guarantees and what it does not.
/// </para>
/// <para>
/// <b>Performance.</b> Multi-pattern checks use <see cref="SearchValues{T}"/> of <see cref="string"/>
/// (SIMD, independent of the pattern count) on .NET 9+, and a <c>FrozenSet&lt;string&gt;</c> scan on .NET 8.
/// Character-set checks use <see cref="SearchValues{T}"/> of <see cref="char"/> on every target.
/// </para>
/// <para>
/// These guards reject; they never sanitize. Null or empty input is a no-op unless a guard says otherwise;
/// add <c>NotNull</c>/<c>NotEmpty</c> upstream when presence is required.
/// </para>
/// </remarks>
public static partial class SecurityGuards
{
    #region Pattern Definitions

    // Comment and operator sequences. They are matched as substrings, because they are punctuation and a
    // word boundary means nothing next to them. The SQL keywords themselves live in SqlKeyword().
    private static readonly string[] SqlTokenList =
    [
        "--", "/*", "*/", "@@"
    ];

    private static readonly string[] XssPatternList =
    [
        "<script", "</script>", "javascript:", "vbscript:",
        "<iframe", "<embed", "<object", "<applet", "<form",
        "expression(", "url(", "eval(", "alert(", "confirm(", "prompt(",
        "document.cookie", "document.write", "window.location",
        "innerHTML", "outerHTML", "document.domain",
        // Event-handler attributes. Matched as "name=" after whitespace before '=' is removed, so the names
        // must be real handlers, not a generic "on*" rule that would reject "online = yes".
        "onabort=", "onafterprint=", "onanimationcancel=", "onanimationend=", "onanimationiteration=",
        "onanimationstart=", "onauxclick=", "onbeforecopy=", "onbeforecut=", "onbeforeinput=",
        "onbeforeprint=", "onbeforetoggle=", "onbeforeunload=", "onbegin=", "onblur=", "oncanplay=",
        "oncanplaythrough=", "onchange=", "onclick=", "onclose=", "oncontextmenu=", "oncopy=",
        "oncuechange=", "oncut=", "ondblclick=", "ondrag=", "ondragend=", "ondragenter=", "ondragleave=",
        "ondragover=", "ondragstart=", "ondrop=", "ondurationchange=", "onend=", "onended=", "onerror=",
        "onfocus=", "onfocusin=", "onfocusout=", "onformdata=", "onfullscreenchange=", "onhashchange=",
        "oninput=", "oninvalid=", "onkeydown=", "onkeypress=", "onkeyup=", "onload=", "onloadeddata=",
        "onloadedmetadata=", "onloadstart=", "onmessage=", "onmousedown=", "onmouseenter=",
        "onmouseleave=", "onmousemove=", "onmouseout=", "onmouseover=", "onmouseup=", "onmousewheel=",
        "onpagehide=", "onpageshow=", "onpaste=", "onpause=", "onplay=", "onplaying=",
        "onpointercancel=", "onpointerdown=", "onpointerenter=", "onpointerleave=", "onpointermove=",
        "onpointerout=", "onpointerover=", "onpointerrawupdate=", "onpointerup=", "onpopstate=",
        "onprogress=", "onratechange=", "onrepeat=", "onreset=", "onresize=", "onscroll=",
        "onscrollend=", "onsearch=", "onseeked=", "onseeking=", "onselect=", "onselectionchange=",
        "onselectstart=", "onshow=", "onstart=", "onstorage=", "onsubmit=", "onsuspend=",
        "ontimeupdate=", "ontoggle=", "ontouchend=", "ontouchmove=", "ontouchstart=",
        "ontransitioncancel=", "ontransitionend=", "ontransitionrun=", "ontransitionstart=",
        "onunhandledrejection=", "onunload=", "onvolumechange=", "onwaiting=",
        "onwebkitanimationend=", "onwebkitanimationiteration=", "onwebkitanimationstart=",
        "onwebkittransitionend=", "onwheel="
    ];

    private static readonly string[] PathTraversalPatternList =
    [
        "..", "../", "..\\", "%2e%2e", "%2e%2e%2f", "%2e%2e/",
        "..%2f", "..%5c", "%2e%2e%5c", "....//", "....\\\\",
        "/etc/passwd", "/etc/shadow", "C:\\Windows", "C:/Windows"
    ];

    // Shell operators and interpreters. AgainstInjection uses this list alone because it screens free text,
    // where quotes, '&', '<', '>', '$' and line breaks are ordinary; AgainstCommandInjection adds
    // ShellMetaCharacters on top.
    private static readonly string[] CommandInjectionPatternList =
    [
        "|", "||", "&&", ";", "`", "$(",
        "/bin/sh", "/bin/bash", "cmd.exe", "powershell",
        "cmd /c", "cmd /k", "cmd.exe /c", "cmd.exe /k",
        "wget ", "curl ", "nc ", "ncat ", "netcat "
    ];

#if NET9_0_OR_GREATER
    private static readonly SearchValues<string> SqlTokens =
        SearchValues.Create(SqlTokenList, StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<string> XssPatterns =
        SearchValues.Create(XssPatternList, StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<string> PathTraversalPatterns =
        SearchValues.Create(PathTraversalPatternList, StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<string> CommandInjectionPatterns =
        SearchValues.Create(CommandInjectionPatternList, StringComparison.OrdinalIgnoreCase);
#else
    private static readonly FrozenSet<string> SqlTokens =
        SqlTokenList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> XssPatterns =
        XssPatternList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> PathTraversalPatterns =
        PathTraversalPatternList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CommandInjectionPatterns =
        CommandInjectionPatternList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
#endif

    private static readonly SearchValues<char> ShellMetaCharacters =
        SearchValues.Create("|&;`$<>'\"\n\r");

    // A character reference, a tab/CR/LF, or an '=' means the browser may see a different string than the
    // raw value, so the XSS list is run again on the normalized form.
    private static readonly SearchValues<char> MarkupObfuscationTriggers =
        SearchValues.Create("&\t\n\r=");

    private static readonly SearchValues<char> LdapDangerousChars =
        SearchValues.Create(['*', '(', ')', '\\', '\0', '/', '\n', '\r']);

    private static readonly SearchValues<char> InvalidFileNameChars =
        SearchValues.Create(Path.GetInvalidFileNameChars());

    // Enough for every legitimate path; a value still changing after this many decodes is itself hostile.
    private const int MaxUrlDecodePasses = 3;

    /// <summary>
    /// Structural SQL shapes: a quoted tautology (<c>'1'='1</c>, <c>''='</c>, <c>'a' = 'a</c>) and a GRANT
    /// statement (<c>GRANT ... TO</c>). The GRANT scan stops at the next GRANT or ';', so each character is
    /// examined a bounded number of times and hostile input cannot make it quadratic.
    /// </summary>
    [GeneratedRegex(@"'\s*=\s*'|\bGRANT\b(?:(?!\bGRANT\b)[^;])*?\bTO\b", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex SqlStructure();

    /// <summary>
    /// SQL keywords, matched at word boundaries. As plain substrings they rejected ordinary words that
    /// merely contain one ("Walter" has ALTER, "executive" has EXEC, "reunion" has UNION, "selection",
    /// "updated", "deleted", "enclosed"). The <c>xp_</c>/<c>sp_</c> procedure prefixes need a boundary only
    /// before them, and the string functions are matched with their opening parenthesis, so "podcast(" and
    /// "research(" are not CAST( and CHAR(.
    /// </summary>
    [GeneratedRegex(
        @"\b(?:SELECT|INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|EXEC|EXECUTE|UNION|TRUNCATE|REVOKE|WAITFOR|DELAY|BENCHMARK|DECLARE|CURSOR|FETCH|CLOSE|DEALLOCATE|INFORMATION_SCHEMA|sysobjects|syscolumns|OPENROWSET|OPENQUERY|OPENDATASOURCE|OPENXML)\b" +
        @"|\b(?:xp|sp)_" +
        @"|\b(?:N?CHAR|N?VARCHAR|CAST|CONVERT|CONCAT)\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SqlKeyword();

    #endregion

    #region Public API

    /// <summary>
    /// Heuristic: throws if <paramref name="value"/> contains a SQL keyword, comment sequence, or quoted
    /// tautology from a fixed list. Null or empty input is a no-op.
    /// </summary>
    /// <remarks>
    /// This is a denylist, not a defence against SQL injection. It misses payloads written in forms it does
    /// not list, and it rejects ordinary text that uses a listed word ("select a plan", "please update your
    /// address"). Keywords match at word boundaries, so a word that merely contains one ("Walter",
    /// "executive", "reunion", "selection") passes.
    /// The defence is parameterized queries (ADO.NET parameters, EF Core, Dapper parameters); never build SQL
    /// by concatenating input, whether or not it passed this guard.
    /// </remarks>
    /// <param name="value">The string to inspect.</param>
    /// <param name="parameterName">Parameter name surfaced on the thrown exception.</param>
    /// <exception cref="ArgumentException">A listed SQL token was found.</exception>
    public static void AgainstSqlInjection(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (ContainsSql(value))
            ThrowInjection(parameterName, "potentially dangerous SQL content");
    }

    /// <summary>
    /// Heuristic: throws if <paramref name="value"/> contains a listed XSS vector: script tags, event-handler
    /// attributes, DOM sinks, <c>javascript:</c>/<c>vbscript:</c> schemes. The list is also checked after
    /// HTML character references are decoded, tab/CR/LF are removed and whitespace before <c>=</c> is dropped,
    /// so <c>jav&amp;#x61;script:</c> and <c>onerror =</c> are caught. Null or empty input is a no-op.
    /// </summary>
    /// <remarks>
    /// This is a denylist, not a defence against cross-site scripting. HTML, SVG and JavaScript offer more
    /// vectors than any list holds (numeric references without a trailing ';', CSS, template injection, ...).
    /// The defence is contextual output encoding (Razor and Blazor encode by default; use the HTML, attribute,
    /// JavaScript or URL encoder that matches where the value lands) plus a Content-Security-Policy. To accept
    /// markup, run it through an HTML sanitizer instead.
    /// </remarks>
    /// <exception cref="ArgumentException">A listed XSS vector was found.</exception>
    public static void AgainstXss(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (ContainsXss(value))
            ThrowInjection(parameterName, "potentially dangerous script content");
    }

    /// <summary>
    /// Throws if <paramref name="value"/> contains a directory traversal sequence or is a rooted path. The
    /// value is checked as given, after repeated URL decoding (<c>%2e</c>, <c>%252e</c>, ...), and after
    /// Unicode NFKC normalization, so <c>.%2e/</c>, <c>%252e%252e%252f</c> and fullwidth <c>．．／</c> are
    /// caught. Null or empty input is a no-op.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rejected: any <c>..</c>; a leading <c>/</c> or <c>\</c> (Unix-absolute, Windows root-relative and UNC
    /// <c>\\server\share</c> paths, which also leak NTLM credentials on Windows); a drive prefix such as
    /// <c>C:</c>; a value that is still changing after three URL decodes; ill-formed UTF-16. The checks are
    /// the same on every operating system.
    /// </para>
    /// <para>
    /// Not guaranteed: that the path stays inside a given directory once combined with it (use
    /// <see cref="AgainstPathEscape"/>, which resolves the path); anything about symbolic links, junctions,
    /// Windows device names (<c>CON</c>, <c>NUL</c>) or alternate data streams. Note that
    /// <c>Path.Combine(root, value)</c> discards <c>root</c> when <c>value</c> is rooted.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">A traversal sequence or rooted path was found.</exception>
    public static void AgainstPathTraversal(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (ContainsPathTraversal(value, rejectRootedPaths: true))
            ThrowInjection(parameterName, "a path traversal sequence or a rooted path");
    }

    /// <summary>
    /// Resolves <paramref name="value"/> against <paramref name="root"/> and throws unless the result lies
    /// strictly inside <paramref name="root"/>. Returns the resolved full path: open that path, not one
    /// re-combined from the inputs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Guaranteed: <c>Path.GetFullPath(Path.Join(root, value))</c> starts with the full path of
    /// <paramref name="root"/> followed by a directory separator, compared ordinally, so every <c>..</c>
    /// segment is resolved the way the operating system resolves it and cannot climb out. Rooted values
    /// (<c>/x</c>, <c>\x</c>, <c>C:\x</c>, <c>C:x</c>, <c>\\server\share</c>), NUL characters, and values that
    /// resolve to <paramref name="root"/> itself are rejected. A root that differs only in letter case from
    /// the resolved path is rejected, never accepted.
    /// </para>
    /// <para>
    /// Not guaranteed: the value is not URL-decoded, so pass it exactly as it will reach the file system;
    /// symbolic links, junctions and mount points inside <paramref name="root"/> can still point outside it;
    /// the file system can change between this check and the open. Windows device names and alternate data
    /// streams inside the root are not rejected.
    /// </para>
    /// </remarks>
    /// <param name="value">A path relative to <paramref name="root"/>.</param>
    /// <param name="root">The directory the path must stay inside. Relative roots resolve against the current directory.</param>
    /// <param name="parameterName">Parameter name surfaced on the thrown exception.</param>
    /// <returns>The resolved full path, inside <paramref name="root"/>.</returns>
    /// <exception cref="ArgumentException">The value is empty, rooted, or resolves outside <paramref name="root"/>.</exception>
    public static string AgainstPathEscape(this string? value, string root, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        if (string.IsNullOrEmpty(value))
            ThrowMessage($"{parameterName} cannot be empty.", parameterName);
        // IsRootedPath covers Path.IsPathRooted on both Windows and Unix, plus UNC and drive-relative forms.
        if (value!.Contains('\0') || IsRootedPath(value))
            ThrowInjection(parameterName, "a rooted path");

        var fullRoot = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(fullRoot))
            fullRoot += Path.DirectorySeparatorChar;

        var fullPath = Path.GetFullPath(Path.Join(fullRoot, value));
        if (fullPath.Length <= fullRoot.Length || !fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
            ThrowInjection(parameterName, "a path that escapes the root directory");

        return fullPath;
    }

    /// <summary>
    /// Heuristic: throws if <paramref name="value"/> contains a shell metacharacter (<c>| &amp; ; ` $ &lt; &gt;
    /// ' "</c>, CR, LF), a known interpreter or download tool (<c>cmd.exe</c>, <c>powershell</c>,
    /// <c>/bin/sh</c>, <c>curl</c>, ...), or starts with <c>-</c>, which a program would read as an option.
    /// Null or empty input is a no-op.
    /// </summary>
    /// <remarks>
    /// This is a denylist, not a defence against command injection. The defence is to start the process
    /// without a shell and pass each argument separately through
    /// <see cref="ProcessStartInfo.ArgumentList"/>, validating arguments against an allow-list of expected
    /// values; end options with <c>--</c> where the program supports it.
    /// </remarks>
    /// <exception cref="ArgumentException">A shell metacharacter, interpreter, or leading '-' was found.</exception>
    public static void AgainstCommandInjection(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (value.AsSpan().IndexOfAny(ShellMetaCharacters) >= 0 ||
            value.TrimStart().StartsWith('-') ||
            ContainsAnyPattern(value, CommandInjectionPatterns))
        {
            ThrowInjection(parameterName, "a potential command injection pattern");
        }
    }

    /// <summary>
    /// Heuristic: throws if <paramref name="value"/> contains an LDAP filter metacharacter
    /// (<c>* ( ) \ /</c>, NUL, CR, LF). Null or empty input is a no-op.
    /// </summary>
    /// <remarks>
    /// This is a denylist aimed at search filters, not a defence against LDAP injection, and it does not cover
    /// distinguished names: <c>jdoe,ou=Admins</c> passes. The defence is escaping, at the sink:
    /// <see cref="Utilities.LdapEncoding.EscapeFilterValue(string)"/> (RFC 4515) for a value placed in a
    /// filter and <see cref="Utilities.LdapEncoding.EscapeDistinguishedNameValue(string)"/> (RFC 4514) for a
    /// value placed in a DN.
    /// </remarks>
    /// <exception cref="ArgumentException">An LDAP filter metacharacter was found.</exception>
    public static void AgainstLdapInjection(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (value.AsSpan().IndexOfAny(LdapDangerousChars) >= 0)
            ThrowInjection(parameterName, "a potential LDAP injection character");
    }

    /// <summary>
    /// Heuristic: throws if <paramref name="value"/> contains XML External Entity (XXE) markers -- a
    /// <c>&lt;!DOCTYPE&gt;</c> declaration, or a combined <c>&lt;!ENTITY&gt;</c>/<c>SYSTEM</c> pair.
    /// Null or empty input is a no-op.
    /// </summary>
    /// <remarks>
    /// The defence is the parser configuration: <see cref="System.Xml.XmlReaderSettings.DtdProcessing"/> set to
    /// <see cref="System.Xml.DtdProcessing.Prohibit"/> and <see cref="System.Xml.XmlReaderSettings.XmlResolver"/>
    /// set to <see langword="null"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">An XXE marker was found.</exception>
    public static void AgainstXxe(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;

        var span = value.AsSpan();
        bool hasDoctype = span.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
        bool hasEntityAndSystem =
            span.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase) &&
            span.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase);

        if (hasDoctype || hasEntityAndSystem)
            ThrowInjection(parameterName, "a potential XXE attack pattern");
    }

    /// <summary>
    /// Heuristic: runs the SQL, XSS and path traversal checks and the shell-operator subset of the command
    /// check in one call, for screening free-text input at an API boundary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a denylist, not a defence; see <see cref="AgainstSqlInjection"/>, <see cref="AgainstXss"/> and
    /// <see cref="AgainstCommandInjection"/> for what each part misses and what the real defence is.
    /// </para>
    /// <para>
    /// Differences from calling the guards one by one: rooted paths are not rejected (text such as
    /// <c>"/help"</c> or <c>"A: yes"</c> is fine here), and the command part checks shell operators and
    /// interpreters only (<c>| || &amp;&amp; ; ` $(</c>, <c>cmd.exe</c>, ...), not quotes, <c>&amp;</c>,
    /// <c>&lt;</c>, <c>&gt;</c>, <c>$</c>, line breaks or a leading <c>-</c>, which are common in names and
    /// prose. Use <see cref="AgainstCommandInjection"/> for a value that reaches a command line.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Any listed injection pattern was found.</exception>
    public static void AgainstInjection(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;

        if (ContainsSql(value))
            ThrowInjection(parameterName, "potentially dangerous SQL content");
        if (ContainsXss(value))
            ThrowInjection(parameterName, "potentially dangerous script content");
        if (ContainsPathTraversal(value, rejectRootedPaths: false))
            ThrowInjection(parameterName, "a path traversal sequence");
        if (ContainsAnyPattern(value, CommandInjectionPatterns))
            ThrowInjection(parameterName, "a potential command injection pattern");
    }

    /// <summary>
    /// Throws if <paramref name="value"/> is not a safe filename -- rejects empty strings, everything
    /// <see cref="AgainstPathTraversal"/> rejects (including rooted paths), and OS-invalid characters (via
    /// <see cref="Path.GetInvalidFileNameChars"/>).
    /// </summary>
    /// <exception cref="ArgumentException">The filename is unsafe.</exception>
    public static void AgainstUnsafeFileName(this string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ThrowMessage($"{parameterName} cannot be empty.", parameterName);

        value.AgainstPathTraversal(parameterName);

        if (value!.AsSpan().IndexOfAny(InvalidFileNameChars) >= 0)
            ThrowInjection(parameterName, "invalid filename characters");
    }

    /// <summary>
    /// Throws unless <paramref name="value"/> is a safe redirect target: a local path, or an absolute
    /// <c>http</c>/<c>https</c> URL whose host is in <paramref name="allowedDomains"/> (or a subdomain of
    /// one). Null or empty input is a no-op.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Follows ASP.NET Core's <c>IsLocalUrl</c>. A local path starts with <c>/</c> not followed by a second
    /// <c>/</c>, or with <c>~/</c> not followed by <c>/</c>. Any whitespace or control character and any
    /// <c>\</c> is rejected, because browsers drop tab/CR/LF and read <c>\</c> as <c>/</c>, which turns
    /// <c>/\t/evil.com</c> or <c>/\evil.com</c> into the protocol-relative <c>//evil.com</c>. Percent-encode
    /// spaces in paths.
    /// </para>
    /// <para>
    /// Everything else must parse as an absolute URL with the <c>http</c> or <c>https</c> scheme, must not be
    /// a UNC path, and must name an allowed host. Relative paths without a leading <c>/</c>
    /// (<c>page.html</c>), <c>javascript:</c>, <c>data:</c>, <c>https:evil.com</c> and protocol-relative
    /// URLs are rejected. The result does not depend on the operating system.
    /// </para>
    /// </remarks>
    /// <param name="value">The redirect URL to validate.</param>
    /// <param name="parameterName">Parameter name surfaced on the thrown exception.</param>
    /// <param name="allowedDomains">
    /// Hosts (bare, no scheme) that absolute URLs may target. An empty array rejects every absolute URL, so
    /// only local paths pass.
    /// </param>
    /// <exception cref="ArgumentException">The value is not a local path or an allowed absolute URL.</exception>
    public static void AgainstOpenRedirect(this string? value, string parameterName, params string[] allowedDomains)
    {
        if (string.IsNullOrEmpty(value)) return;

        foreach (char c in value)
        {
            if (c <= ' ' || char.IsControl(c) || c == '\\')
                ThrowInjection(parameterName, "a potential open redirect (whitespace, control character or backslash)");
        }

        int localPathStart = value.StartsWith("~/", StringComparison.Ordinal) ? 1 : 0;
        if (value[localPathStart] == '/')
        {
            if (value.Length > localPathStart + 1 && value[localPathStart + 1] == '/')
                ThrowInjection(parameterName, "a potential open redirect (protocol-relative URL)");
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.IsUnc)
        {
            ThrowMessage($"{parameterName} must be a local path or an absolute http(s) URL.", parameterName);
        }

        foreach (var domain in allowedDomains)
        {
            if (string.IsNullOrEmpty(domain)) continue;
            if (uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        ThrowMessage(
            $"{parameterName} redirects to an untrusted domain '{uri.Host}'.",
            parameterName);
    }

    #endregion

    #region Internal matching

    private static bool ContainsSql(string value) =>
        ContainsAnyPattern(value, SqlTokens) || SqlKeyword().IsMatch(value) || SqlStructure().IsMatch(value);

    private static bool ContainsXss(string value)
    {
        if (ContainsAnyPattern(value, XssPatterns)) return true;
        if (value.AsSpan().IndexOfAny(MarkupObfuscationTriggers) < 0) return false;
        return ContainsAnyPattern(NormalizeMarkup(value), XssPatterns);
    }

    /// <summary>
    /// The value as a browser reads it inside an HTML attribute: character references decoded (including the
    /// HTML5 names <c>&amp;colon;</c>, <c>&amp;Tab;</c>, <c>&amp;NewLine;</c>, which
    /// <see cref="WebUtility.HtmlDecode(string)"/> does not know), tab/CR/LF removed as the URL parser removes
    /// them, and whitespace before <c>=</c> dropped as the attribute parser skips it.
    /// </summary>
    private static string NormalizeMarkup(string value)
    {
        var decoded = WebUtility.HtmlDecode(value)
            .Replace("&colon;", ":", StringComparison.Ordinal)
            .Replace("&Tab;", "", StringComparison.Ordinal)
            .Replace("&NewLine;", "", StringComparison.Ordinal);

        var builder = new StringBuilder(decoded.Length);
        foreach (char c in decoded)
        {
            if (c is '\t' or '\n' or '\r') continue;
            if (c == '=')
            {
                while (builder.Length > 0 && char.IsWhiteSpace(builder[^1]))
                    builder.Length--;
            }
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static bool ContainsPathTraversal(string value, bool rejectRootedPaths)
    {
        var current = value;
        for (int pass = 0; ; pass++)
        {
            string normalized;
            try
            {
                normalized = current.Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                // Ill-formed UTF-16 cannot be normalized, so fullwidth dots in it would go unseen.
                return true;
            }

            if (ContainsAnyPattern(normalized, PathTraversalPatterns) ||
                (rejectRootedPaths && IsRootedPath(normalized)))
            {
                return true;
            }

            var decoded = Uri.UnescapeDataString(normalized);
            if (decoded == normalized) return false;
            if (pass == MaxUrlDecodePasses) return true;
            current = decoded;
        }
    }

    // Deliberately independent of the host OS: a value checked on Linux may be used on Windows.
    private static bool IsRootedPath(string path) =>
        path.Length > 0 &&
        (path[0] is '/' or '\\' || (path.Length > 1 && path[1] == ':' && char.IsAsciiLetter(path[0])));

    /// <summary>
    /// Multi-pattern contains check. Dispatches to the fastest primitive available for the TFM.
    /// </summary>
#if NET9_0_OR_GREATER
    private static bool ContainsAnyPattern(string value, SearchValues<string> patterns)
        => value.AsSpan().ContainsAny(patterns);
#else
    private static bool ContainsAnyPattern(string value, FrozenSet<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
#endif

    #endregion

    #region Throw helpers

    [DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowInjection(string parameterName, string reason) =>
        throw new ArgumentException($"{parameterName} contains {reason}.", parameterName);

    [DoesNotReturn]
    [StackTraceHidden]
    private static void ThrowMessage(string message, string parameterName) =>
        throw new ArgumentException(message, parameterName);

    #endregion
}
