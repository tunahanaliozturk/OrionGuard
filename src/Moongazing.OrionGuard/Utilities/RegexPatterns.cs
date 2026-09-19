namespace Moongazing.OrionGuard.Utilities
{
    /// <summary>
    /// Provides commonly used regex patterns.
    /// </summary>
    [Obsolete("Use GeneratedRegexPatterns for source-generated, NativeAOT-compatible regex. This class will be removed in v7.0.")]
    public static class RegexPatterns
    {
        public const string Email = GeneratedRegexPatterns.EmailPattern;
        public const string Url = @"^(https?|ftp)://[^\s/$.?#].[^\s]*\z";
        public const string PhoneNumber = @"^\+?[1-9][0-9]{1,14}\z";
    }
}
