using System.Collections.Frozen;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Security-focused validation guards for detecting injection attacks and other security threats.
/// </summary>
public static class SecurityGuards
{
    private static readonly FrozenSet<string> SqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE",
        "EXEC", "EXECUTE", "UNION", "TRUNCATE", "GRANT", "REVOKE",
        "xp_", "sp_", "INFORMATION_SCHEMA", "sysobjects", "syscolumns",
        "--", ";--", "/*", "*/", "@@", "WAITFOR", "DELAY", "BENCHMARK",
        "CHAR(", "NCHAR(", "VARCHAR(", "CAST(", "CONVERT(", "CONCAT(",
        "DECLARE", "CURSOR", "FETCH", "OPEN", "CLOSE", "DEALLOCATE"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> XssPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "<script", "</script>", "javascript:", "vbscript:", "onload=", "onerror=",
        "onclick=", "onmouseover=", "onfocus=", "onblur=", "onsubmit=",
        "onchange=", "onkeyup=", "onkeydown=", "onkeypress=",
        "<iframe", "<embed", "<object", "<applet", "<form",
        "expression(", "url(", "eval(", "alert(", "confirm(", "prompt(",
        "document.cookie", "document.write", "window.location",
        "innerHTML", "outerHTML", "document.domain"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> PathTraversalPatterns = new HashSet<string>(StringComparer.Ordinal)
    {
        "..", "../", "..\\", "%2e%2e", "%2e%2e%2f", "%2e%2e/",
        "..%2f", "..%5c", "%2e%2e%5c", "....//", "....\\\\",
        "/etc/passwd", "/etc/shadow", "C:\\Windows", "C:/Windows"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> CommandInjectionPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "|", "||", "&&", ";", "`", "$(", "$(",
        "/bin/sh", "/bin/bash", "cmd.exe", "powershell",
        "cmd /c", "cmd /k", "cmd.exe /c", "cmd.exe /k",
        "wget ", "curl ", "nc ", "ncat ", "netcat "
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validates that a string does not contain SQL injection patterns.
    /// </summary>
    public static void AgainstSqlInjection(this string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;

        var normalized = value.Replace("'", "''"); // Check pre-escape
        foreach (var keyword in SqlKeywords)
        {
            if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"{parameterName} contains potentially dangerous SQL content.",
                    parameterName);
            }
        }
    }

    /// <summary>
    /// Validates that a string does not contain XSS (Cross-Site Scripting) patterns.
    /// </summary>
    public static void AgainstXss(this string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;

        foreach (var pattern in XssPatterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"{parameterName} contains potentially dangerous script content.",
                    parameterName);
            }
        }
    }

    /// <summary>
    /// Validates that a string does not contain path traversal sequences.
    /// </summary>
    public static void AgainstPathTraversal(this string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;

        foreach (var pattern in PathTraversalPatterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"{parameterName} contains a path traversal sequence.",
                    parameterName);
            }
        }
    }

    /// <summary>
    /// Validates that a string does not contain command injection patterns.
    /// </summary>
    public static void AgainstCommandInjection(this string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;

        foreach (var pattern in CommandInjectionPatterns)
        {
            if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"{parameterName} contains a potential command injection pattern.",
                    parameterName);
            }
        }
    }

    /// <summary>
    /// Validates that a string does not contain LDAP injection patterns.
    /// </summary>
    public static void AgainstLdapInjection(this string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;

        ReadOnlySpan<char> dangerous = ['*', '(', ')', '\\', '\0', '/', '\n', '\r'];
        foreach (var c in value)
        {
            if (dangerous.Contains(c))
            {
                throw new ArgumentException(
                    $"{parameterName} contains a potential LDAP injection character.",
                    parameterName);
            }
        }
    }

    /// <summary>
    /// Validates that a string does not contain XML External Entity (XXE) attack patterns.
    /// </summary>
    public static void AgainstXxe(this string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)) return;

        if (value.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase) && value.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"{parameterName} contains a potential XXE attack pattern.",
                parameterName);
        }
    }

    /// <summary>
    /// Validates that a string is safe from all common injection attacks.
    /// Combines SQL, XSS, path traversal, and command injection checks.
    /// </summary>
    public static void AgainstInjection(this string value, string parameterName)
    {
        value.AgainstSqlInjection(parameterName);
        value.AgainstXss(parameterName);
        value.AgainstPathTraversal(parameterName);
        value.AgainstCommandInjection(parameterName);
    }

    /// <summary>
    /// Validates that a filename is safe (no path traversal, only allowed characters).
    /// </summary>
    public static void AgainstUnsafeFileName(this string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be empty.", parameterName);
        }

        value.AgainstPathTraversal(parameterName);

        var invalidChars = Path.GetInvalidFileNameChars();
        if (value.IndexOfAny(invalidChars) >= 0)
        {
            throw new ArgumentException(
                $"{parameterName} contains invalid filename characters.",
                parameterName);
        }
    }

    /// <summary>
    /// Validates that a URL does not contain open redirect patterns.
    /// </summary>
    public static void AgainstOpenRedirect(this string value, string parameterName, params string[] allowedDomains)
    {
        if (string.IsNullOrEmpty(value)) return;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            // Relative URLs are generally safe
            if (value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith("/\\", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{parameterName} contains a potential open redirect (protocol-relative URL).",
                    parameterName);
            }
            return;
        }

        if (allowedDomains.Length > 0 &&
            !allowedDomains.Any(d => uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                                    uri.Host.EndsWith($".{d}", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"{parameterName} redirects to an untrusted domain '{uri.Host}'.",
                parameterName);
        }
    }
}
