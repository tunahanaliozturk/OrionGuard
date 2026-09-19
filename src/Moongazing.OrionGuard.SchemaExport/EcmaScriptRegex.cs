namespace Moongazing.OrionGuard.SchemaExport;

/// <summary>
/// Screens a .NET regex before it is copied into an artifact. A JSON Schema <c>pattern</c> is an
/// ECMAScript regex with no flags, so a .NET-only construct either makes the consumer reject the
/// schema outright or, worse, makes it silently match something the server does not.
/// </summary>
/// <remarks>
/// The screen is deliberately pessimistic: it recognises the group and escape forms it can vouch
/// for and rejects everything else. A false rejection costs one constraint and says so in
/// <c>x-orionguard-unsupported</c>; a false acceptance ships a lie.
/// </remarks>
internal static class EcmaScriptRegex
{
    /// <summary>
    /// True when the pattern uses only constructs an ECMAScript engine reads the same way .NET does.
    /// </summary>
    internal static bool IsPortable(string pattern)
    {
        var inCharacterClass = false;

        for (var index = 0; index < pattern.Length; index++)
        {
            var current = pattern[index];

            if (current == '\\')
            {
                if (index + 1 >= pattern.Length)
                {
                    return false;
                }

                // \A \Z \z \G are .NET anchors ECMAScript does not have; \p and \P need the "u"
                // flag, which a JSON Schema pattern has no way to set.
                if ("AZzGpP".IndexOf(pattern[index + 1]) >= 0)
                {
                    return false;
                }

                index++;
                continue;
            }

            if (inCharacterClass)
            {
                if (current == ']')
                {
                    inCharacterClass = false;
                }
                else if (current == '[' && index > 0 && pattern[index - 1] == '-')
                {
                    // [a-z-[aeiou]] is .NET character class subtraction. ECMAScript reads the inner
                    // bracket as a literal and matches a strictly larger set.
                    return false;
                }

                continue;
            }

            if (current == '[')
            {
                inCharacterClass = true;
                continue;
            }

            if (current == '(' && index + 1 < pattern.Length && pattern[index + 1] == '?'
                && !IsPortableGroup(pattern, index + 2))
            {
                return false;
            }
        }

        // An unterminated class is not a valid .NET regex either; refuse rather than guess.
        return !inCharacterClass;
    }

    /// <summary>
    /// Classifies the group that opens at <paramref name="index"/>, the character after <c>(?</c>.
    /// </summary>
    private static bool IsPortableGroup(string pattern, int index)
    {
        if (index >= pattern.Length)
        {
            return false;
        }

        return pattern[index] switch
        {
            ':' or '=' or '!' => true,

            // Lookbehind is shared; (?<name> and (?<a-b> (balancing groups) are not.
            '<' => index + 1 < pattern.Length && (pattern[index + 1] is '=' or '!'),

            // Everything else: (?'name' , (?> atomic, (?( conditional, (?# comment, and every
            // inline-option form such as (?i) or (?is:...).
            _ => false
        };
    }
}
