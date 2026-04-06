using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Utilities;

namespace Moongazing.OrionGuard.Benchmarks;

/// <summary>
/// Compares old RegexCache.IsMatch vs new GeneratedRegexPatterns source-generated regex.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class RegexBenchmarks
{
    private const string ValidEmail = "test@example.com";
    private const string ValidPhone = "+14155551234";
    private const string ValidAlphaNumeric = "Hello123";

    // --- Email pattern ---

    [Benchmark]
    public bool RegexCache_Email()
    {
        return RegexCache.IsMatch(ValidEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }

    [Benchmark]
    public bool GeneratedRegex_Email()
    {
        return GeneratedRegexPatterns.Email().IsMatch(ValidEmail);
    }

    // --- PhoneNumber pattern ---

    [Benchmark]
    public bool RegexCache_PhoneNumber()
    {
        return RegexCache.IsMatch(ValidPhone, @"^\+?[1-9]\d{1,14}$");
    }

    [Benchmark]
    public bool GeneratedRegex_PhoneNumber()
    {
        return GeneratedRegexPatterns.PhoneNumber().IsMatch(ValidPhone);
    }

    // --- AlphaNumeric pattern ---

    [Benchmark]
    public bool RegexCache_AlphaNumeric()
    {
        return RegexCache.IsMatch(ValidAlphaNumeric, @"^[a-zA-Z0-9]+$");
    }

    [Benchmark]
    public bool GeneratedRegex_AlphaNumeric()
    {
        return GeneratedRegexPatterns.AlphaNumeric().IsMatch(ValidAlphaNumeric);
    }
}
