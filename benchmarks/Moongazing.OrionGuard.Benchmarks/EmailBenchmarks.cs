using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Utilities;

namespace Moongazing.OrionGuard.Benchmarks;

/// <summary>
/// Compares email validation performance: FastGuard span-based vs Guard regex vs raw regex.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class EmailBenchmarks
{
    private const string ValidEmail = "test@example.com";
    private static readonly Regex CompiledEmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    [Benchmark(Baseline = true)]
    public bool RawCompiledRegex_Email()
    {
        return CompiledEmailRegex.IsMatch(ValidEmail);
    }

    [Benchmark]
    public bool GeneratedRegex_Email()
    {
        return GeneratedRegexPatterns.Email().IsMatch(ValidEmail);
    }

    [Benchmark]
    public void Guard_AgainstInvalidEmail()
    {
        Guard.AgainstInvalidEmail(ValidEmail, nameof(ValidEmail));
    }

    [Benchmark]
    public string FastGuard_Email_SpanBased()
    {
        return FastGuard.Email(ValidEmail, nameof(ValidEmail));
    }

    [Benchmark]
    public void Ensure_That_Email()
    {
        Ensure.That(ValidEmail).Email();
    }
}
