using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using FluentValidation.Results;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Benchmarks.Comparison;

/// <summary>
/// Scenario (d): construct a validator and validate once (valid input).
/// <list type="bullet">
/// <item>The command-line job (e.g. <c>--job short</c>) measures it in a warm process: the per-request cost
/// when a validator is registered transient or scoped. OrionGuard's AddOrionGuard registers validators as
/// transient; FluentValidation's AddValidatorsFromAssembly defaults to scoped.</item>
/// <item>The <c>ColdStart</c> job measures the very first call in a fresh process (JIT, type loading and
/// first-time caches included), once per launch: the cost paid at application start-up.</item>
/// </list>
/// Validate.For and [GenerateValidator] have no construction step, so they are not in this table.
/// The validators are the ones scenario (a) checks for parity; no parity call runs here, because it would
/// warm the code the ColdStart job must see cold.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.ColdStart, launchCount: 15, warmupCount: 0, iterationCount: 1, id: "ColdStart")]
public class ConstructionComparisonBenchmarks
{
    private CustomerDto _dto = null!;

    [GlobalSetup]
    public void Setup() => _dto = CustomerDto.Valid();

    [Benchmark(Baseline = true)]
    public ValidationResult FluentValidation_NewAndValidate() => new FvCustomerValidator().Validate(_dto);

    [Benchmark]
    public GuardResult OrionGuard_AbstractValidator_NewAndValidate() => new OgCustomerValidator().Validate(_dto);

    [Benchmark]
    public GuardResult OrionGuard_FluentStyleValidator_NewAndValidate() => new OgCompatCustomerValidator().Validate(_dto);
}
