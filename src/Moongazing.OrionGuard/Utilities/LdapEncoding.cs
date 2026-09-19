using System.Text;

namespace Moongazing.OrionGuard.Utilities;

/// <summary>
/// Escapes values that are placed into an LDAP search filter (RFC 4515) or a distinguished name
/// (RFC 4514). This is the actual defence against LDAP injection: escape at the sink, rather than trying
/// to reject hostile-looking input with <c>AgainstLdapInjection</c>, which is only a heuristic.
/// </summary>
public static class LdapEncoding
{
    /// <summary>
    /// Escapes a value for the assertion value of a search filter, per RFC 4515 section 3: <c>*</c>,
    /// <c>(</c>, <c>)</c>, <c>\</c> and NUL become <c>\2a</c>, <c>\28</c>, <c>\29</c>, <c>\5c</c> and
    /// <c>\00</c>. Everything else, including non-ASCII text, is left as it is, which RFC 4515 allows for a
    /// filter carried as UTF-8.
    /// </summary>
    /// <param name="value">The value to escape. It is escaped, never rejected.</param>
    /// <returns>The value as it may be written inside <c>(attribute=&lt;value&gt;)</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// var filter = $"(cn={LdapEncoding.EscapeFilterValue(userInput)})";
    /// </code>
    /// </example>
    public static string EscapeFilterValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '*': builder.Append("\\2a"); break;
                case '(': builder.Append("\\28"); break;
                case ')': builder.Append("\\29"); break;
                case '\\': builder.Append("\\5c"); break;
                case '\0': builder.Append("\\00"); break;
                default: builder.Append(c); break;
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Escapes a value for one attribute value of a distinguished name, per RFC 4514 section 2.4:
    /// <c>"</c>, <c>+</c>, <c>,</c>, <c>;</c>, <c>&lt;</c>, <c>&gt;</c> and <c>\</c> are escaped with a
    /// backslash anywhere, a leading space or <c>#</c> and a trailing space are escaped, and NUL becomes
    /// <c>\00</c>. Escaping the separators is what stops a value such as <c>jdoe,ou=Admins</c> from adding
    /// a relative distinguished name of its own.
    /// </summary>
    /// <param name="value">The value to escape. It is escaped, never rejected.</param>
    /// <returns>The value as it may be written after <c>&lt;attribute&gt;=</c> in a DN.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// var dn = $"CN={LdapEncoding.EscapeDistinguishedNameValue(userInput)},OU=People,DC=example,DC=net";
    /// </code>
    /// </example>
    public static string EscapeDistinguishedNameValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\0')
            {
                builder.Append("\\00");
                continue;
            }
            if (c is '"' or '+' or ',' or ';' or '<' or '>' or '\\'
                || (i == 0 && c is ' ' or '#')
                || (i == value.Length - 1 && c == ' '))
            {
                builder.Append('\\');
            }
            builder.Append(c);
        }
        return builder.ToString();
    }
}
