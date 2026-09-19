using System.Text.RegularExpressions;

namespace Moongazing.OrionGuard.Utilities;

/// <summary>
/// Format checks shared by every guard and validator, so the same input gets the same answer from
/// <c>Guard</c>, <c>Ensure</c>, <c>FastGuard</c>, the attribute and validator APIs, and the dynamic rules.
/// </summary>
internal static class FormatRules
{
    /// <summary>
    /// Evaluates <paramref name="regex"/> for a rule that must report a validation failure. A match that
    /// runs past the regex timeout counts as a mismatch: the input is rejected as invalid instead of
    /// surfacing <see cref="RegexMatchTimeoutException"/> to a caller that asked for a validation result.
    /// </summary>
    internal static bool IsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    internal static bool IsEmail(string value) => IsMatch(GeneratedRegexPatterns.Email(), value);

    /// <summary>
    /// True only for an absolute <c>http</c> or <c>https</c> URL. <c>javascript:</c>, <c>data:</c>,
    /// <c>file:</c> and every other scheme are rejected, as are Unix paths that <see cref="Uri"/> would
    /// otherwise parse as implicit <c>file:</c> URIs.
    /// </summary>
    internal static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
