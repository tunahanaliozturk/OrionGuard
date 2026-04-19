using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Benchmarks;

/// <summary>
/// Benchmarks SecurityGuards hot-path performance with safe input (no exceptions thrown).
/// Measures the cost of scanning FrozenSet patterns on valid, non-malicious strings.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class SecurityGuardBenchmarks
{
    private string _safeShortInput = null!;
    private string _safeMediumInput = null!;
    private string _safeLongInput = null!;

    [GlobalSetup]
    public void Setup()
    {
        _safeShortInput = "HelloWorld";
        _safeMediumInput = "This is a perfectly normal user comment with no malicious content at all.";
        _safeLongInput = string.Join(" ", Enumerable.Repeat(
            "Lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor", 20));
    }

    // --- SQL Injection checks ---

    [Benchmark]
    public void AgainstSqlInjection_Short()
    {
        _safeShortInput.AgainstSqlInjection(nameof(_safeShortInput));
    }

    [Benchmark]
    public void AgainstSqlInjection_Medium()
    {
        _safeMediumInput.AgainstSqlInjection(nameof(_safeMediumInput));
    }

    [Benchmark]
    public void AgainstSqlInjection_Long()
    {
        _safeLongInput.AgainstSqlInjection(nameof(_safeLongInput));
    }

    // --- XSS checks ---

    [Benchmark]
    public void AgainstXss_Short()
    {
        _safeShortInput.AgainstXss(nameof(_safeShortInput));
    }

    [Benchmark]
    public void AgainstXss_Medium()
    {
        _safeMediumInput.AgainstXss(nameof(_safeMediumInput));
    }

    [Benchmark]
    public void AgainstXss_Long()
    {
        _safeLongInput.AgainstXss(nameof(_safeLongInput));
    }

    // --- Combined injection check ---

    [Benchmark]
    public void AgainstInjection_Short()
    {
        _safeShortInput.AgainstInjection(nameof(_safeShortInput));
    }

    [Benchmark]
    public void AgainstInjection_Medium()
    {
        _safeMediumInput.AgainstInjection(nameof(_safeMediumInput));
    }
}
