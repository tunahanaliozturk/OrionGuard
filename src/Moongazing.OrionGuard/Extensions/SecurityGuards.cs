using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
#if !NET9_0_OR_GREATER
using System.Collections.Frozen;
#endif

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Security-focused validation guards for detecting injection attacks at application boundaries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Performance.</b> All multi-pattern checks use the fastest primitive available for the
/// current target framework:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>.NET 9+</b>: <see cref="SearchValues{T}"/> of <see cref="string"/> backed by the
/// <i>Teddy</i> algorithm. A single <c>ContainsAny</c> call probes all patterns in parallel
/// using SIMD, independent of the pattern count.
/// </description></item>
/// <item><description>
/// <b>.NET 8</b>: <c>FrozenSet&lt;string&gt;</c> with a sequential scan. No string-based
/// <see cref="SearchValues{T}"/> is available on this target.
/// </description></item>
/// </list>
/// <para>
/// Character-set checks (LDAP, filename) always use <see cref="SearchValues{T}"/> of
/// <see cref="char"/>, which is available on all supported targets.
/// </para>
/// <para>
/// <b>Design.</b> These guards <i>reject</i> unsafe input -- they do not sanitize. Output
/// encoding remains the responsibility of the rendering layer (Razor, JSON serializer, etc.).
/// Null or empty input is a no-op; callers should add <c>NotNull</c>/<c>NotEmpty</c> upstream
/// if presence is required.
/// </para>
/// </remarks>
public static class SecurityGuards
{
    #region Pattern Definitions

    private static readonly string[] SqlKeywordList =
    [
        "SELECT", "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE",
        "EXEC", "EXECUTE", "UNION", "TRUNCATE", "GRANT", "REVOKE",
        "xp_", "sp_", "INFORMATION_SCHEMA", "sysobjects", "syscolumns",
        "--", ";--", "/*", "*/", "@@", "WAITFOR", "DELAY", "BENCHMARK",
        "CHAR(", "NCHAR(", "VARCHAR(", "CAST(", "CONVERT(", "CONCAT(",
        "DECLARE", "CURSOR", "FETCH", "OPEN", "CLOSE", "DEALLOCATE"
    ];

    private static readonly string[] XssPatternList =
    [
        "<script", "</script>", "javascript:", "vbscript:", "onload=", "onerror=",
        "onclick=", "onmouseover=", "onfocus=", "onblur=", "onsubmit=",
        "onchange=", "onkeyup=", "onkeydown=", "onkeypress=",
        "<iframe", "<embed", "<object", "<applet", "<form",
        "expression(", "url(", "eval(", "alert(", "confirm(", "prompt(",
        "document.cookie", "document.write", "window.location",
        "innerHTML", "outerHTML", "document.domain"
    ];

    private static readonly string[] PathTraversalPatternList =
    [
        "..", "../", "..\\", "%2e%2e", "%2e%2e%2f", "%2e%2e/",
        "..%2f", "..%5c", "%2e%2e%5c", "....//", "....\\\\",
        "/etc/passwd", "/etc/shadow", "C:\\Windows", "C:/Windows"
    ];

    private static readonly string[] CommandInjectionPatternList =
    [
        "|", "||", "&&", ";", "`", "$(",
        "/bin/sh", "/bin/bash", "cmd.exe", "powershell",
        "cmd /c", "cmd /k", "cmd.exe /c", "cmd.exe /k",
        "wget ", "curl ", "nc ", "ncat ", "netcat "
    ];

#if NET9_0_OR_GREATER
    private static readonly SearchValues<string> SqlKeywords =
        SearchValues.Create(SqlKeywordList, StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<string> XssPatterns =
        SearchValues.Create(XssPatternList, StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<string> PathTraversalPatterns =
        SearchValues.Create(PathTraversalPatternList, StringComparison.OrdinalIgnoreCase);

    private static readonly SearchValues<string> CommandInjectionPatterns =
        SearchValues.Create(CommandInjectionPatternList, StringComparison.OrdinalIgnoreCase);
#else
    private static readonly FrozenSet<string> SqlKeywords =
        SqlKeywordList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> XssPatterns =
        XssPatternList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> PathTraversalPatterns =
        PathTraversalPatternList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CommandInjectionPatterns =
        CommandInjectionPatternList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
#endif

    private static readonly SearchValues<char> LdapDangerousChars =
        SearchValues.Create(['*', '(', ')', '\\', '\0', '/', '\n', '\r']);

    private static readonly SearchValues<char> InvalidFileNameChars =
        SearchValues.Create(Path.GetInvalidFileNameChars());

    #endregion

    #region Public API

    /// <summary>
    /// Throws if <paramref name="value"/> contains SQL injection patterns
    /// (34 keywords, operators, and comment sequences). Null or empty input is a no-op.
    /// </summary>
    /// <param name="value">The string to inspect.</param>
    /// <param name="parameterName">Parameter name surfaced on the thrown exception.</param>
    /// <exception cref="ArgumentException">A SQL keyword or injection token was found.</exception>
    public static void AgainstSqlInjection(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (ContainsAnyPattern(value, SqlKeywords))
            ThrowInjection(parameterName, "potentially dangerous SQL content");
    }

    /// <summary>
    /// Throws if <paramref name="value"/> contains XSS vectors (script tags, event handlers,
    /// DOM sinks, <c>javascript:</c>/<c>vbscript:</c> schemes). Null or empty input is a no-op.
    /// </summary>
    /// <exception cref="ArgumentException">An XSS vector was found.</exception>
    public static void AgainstXss(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (ContainsAnyPattern(value, XssPatterns))
            ThrowInjection(parameterName, "potentially dangerous script content");
    }

    /// <summary>
    /// Throws if <paramref name="value"/> contains directory traversal sequences, including
    /// common URL-encoded variants (<c>../</c>, <c>..\</c>, <c>%2e%2e</c>, etc.).
    /// Null or empty input is a no-op.
    /// </summary>
    /// <exception cref="ArgumentException">A path traversal sequence was found.</exception>
    public static void AgainstPathTraversal(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (ContainsAnyPattern(value, PathTraversalPatterns))
            ThrowInjection(parameterName, "a path traversal sequence");
    }

    /// <summary>
    /// Throws if <paramref name="value"/> contains OS command injection patterns
    /// (shell metacharacters, pipe operators, known interpreters). Null or empty input is a no-op.
    /// </summary>
    /// <exception cref="ArgumentException">A command injection pattern was found.</exception>
    public static void AgainstCommandInjection(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (ContainsAnyPattern(value, CommandInjectionPatterns))
            ThrowInjection(parameterName, "a potential command injection pattern");
    }

    /// <summary>
    /// Throws if <paramref name="value"/> contains any LDAP-special character.
    /// Uses <see cref="SearchValues{Char}"/> for SIMD-accelerated scanning.
    /// Null or empty input is a no-op.
    /// </summary>
    /// <exception cref="ArgumentException">An LDAP-special character was found.</exception>
    public static void AgainstLdapInjection(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (value.AsSpan().IndexOfAny(LdapDangerousChars) >= 0)
            ThrowInjection(parameterName, "a potential LDAP injection character");
    }

    /// <summary>
    /// Throws if <paramref name="value"/> contains XML External Entity (XXE) attack markers --
    /// a <c>&lt;!DOCTYPE&gt;</c> declaration, or a combined <c>&lt;!ENTITY&gt;</c>/<c>SYSTEM</c>
    /// pair. Null or empty input is a no-op.
    /// </summary>
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
    /// Runs all composite injection checks (SQL + XSS + path traversal + command injection) in
    /// a single pass. Prefer this guard at the API boundary unless you need a specific error code.
    /// </summary>
    /// <remarks>
    /// On .NET 9+ each check uses a single SIMD-accelerated <c>ContainsAny</c>, so four
    /// <see cref="SearchValues{T}"/> probes run against the same span without re-materializing it.
    /// </remarks>
    /// <exception cref="ArgumentException">Any injection pattern was found.</exception>
    public static void AgainstInjection(this string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;

        if (ContainsAnyPattern(value, SqlKeywords))
            ThrowInjection(parameterName, "potentially dangerous SQL content");
        if (ContainsAnyPattern(value, XssPatterns))
            ThrowInjection(parameterName, "potentially dangerous script content");
        if (ContainsAnyPattern(value, PathTraversalPatterns))
            ThrowInjection(parameterName, "a path traversal sequence");
        if (ContainsAnyPattern(value, CommandInjectionPatterns))
            ThrowInjection(parameterName, "a potential command injection pattern");
    }

    /// <summary>
    /// Throws if <paramref name="value"/> is not a safe filename -- rejects empty strings,
    /// path traversal sequences, and OS-invalid characters (via
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
    /// Throws if <paramref name="value"/> is a redirect URL that targets a host outside the
    /// supplied <paramref name="allowedDomains"/> allow-list. Protocol-relative URLs
    /// (<c>//evil.com</c>, <c>/\evil.com</c>) are always rejected.
    /// </summary>
    /// <param name="value">The redirect URL to validate.</param>
    /// <param name="parameterName">Parameter name surfaced on the thrown exception.</param>
    /// <param name="allowedDomains">
    /// Hosts (bare, no scheme) that are allowed. An empty array allows any absolute URL,
    /// which is usually <i>not</i> what you want -- supply at least one.
    /// </param>
    /// <exception cref="ArgumentException">The URL redirects to an untrusted host.</exception>
    public static void AgainstOpenRedirect(this string? value, string parameterName, params string[] allowedDomains)
    {
        if (string.IsNullOrEmpty(value)) return;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (value.StartsWith("//", StringComparison.Ordinal) ||
                value.StartsWith("/\\", StringComparison.Ordinal))
            {
                ThrowInjection(parameterName, "a potential open redirect (protocol-relative URL)");
            }
            return;
        }

        if (allowedDomains.Length == 0) return;

        foreach (var domain in allowedDomains)
        {
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
