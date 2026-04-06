using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Benchmarks;

/// <summary>
/// Compares null-check performance across Guard, Ensure, FastGuard, and raw if-throw.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class NullCheckBenchmarks
{
    private object _validObject = null!;
    private string _validString = null!;

    [GlobalSetup]
    public void Setup()
    {
        _validObject = new object();
        _validString = "not-null";
    }

    [Benchmark(Baseline = true)]
    public object RawIfThrow_NotNull()
    {
        if (_validObject is null)
            throw new ArgumentNullException(nameof(_validObject));
        return _validObject;
    }

    [Benchmark]
    public void Guard_AgainstNull()
    {
        Guard.AgainstNull(_validObject, nameof(_validObject));
    }

    [Benchmark]
    public void Ensure_That_NotNull()
    {
        Ensure.That(_validObject).NotNull();
    }

    [Benchmark]
    public object FastGuard_NotNull()
    {
        return FastGuard.NotNull(_validObject, nameof(_validObject));
    }

    [Benchmark]
    public void Guard_AgainstNullOrEmpty_ValidString()
    {
        Guard.AgainstNullOrEmpty(_validString, nameof(_validString));
    }

    [Benchmark]
    public string FastGuard_NotNullOrEmpty_ValidString()
    {
        return FastGuard.NotNullOrEmpty(_validString, nameof(_validString));
    }

    [Benchmark]
    public string Ensure_NotNullOrEmpty_ValidString()
    {
        return Ensure.NotNullOrEmpty(_validString);
    }
}
