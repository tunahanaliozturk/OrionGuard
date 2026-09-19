using BenchmarkDotNet.Attributes;
using FluentValidation.Results;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Benchmarks.Comparison;

/// <summary>
/// Scenario (e): the 5 sync rules of scenario (a) plus one async rule (an "email available" lookup that
/// completes synchronously), resolved through each library's async entry point.
/// FluentStyleValidator is absent: it has no async rules (its ValidateAsync wraps the sync path).
/// </summary>
[MemoryDiagnoser]
public class AsyncComparisonBenchmarks
{
    private FvCustomerAsyncValidator _fluentValidation = null!;
    private OgCustomerAsyncValidator _orionGuard = null!;
    private CustomerDto _dto = null!;

    [Params("Valid", "Invalid")]
    public string Input { get; set; } = "Valid";

    [GlobalSetup]
    public void Setup()
    {
        _fluentValidation = new FvCustomerAsyncValidator();
        _orionGuard = new OgCustomerAsyncValidator();
        _dto = Input == "Valid" ? CustomerDto.Valid() : CustomerDto.Invalid();

        var expected = Input == "Valid" ? 0 : 6;
        Parity.Expect(nameof(FluentValidation_ValidateAsync), FluentValidation_ValidateAsync().GetAwaiter().GetResult().Errors.Count, expected);
        Parity.Expect(nameof(OrionGuard_AbstractValidator_ValidateAsync), OrionGuard_AbstractValidator_ValidateAsync().GetAwaiter().GetResult().AllIssues.Count, expected);
        Parity.Expect(nameof(OrionGuard_ValidateFor_ToResultAsync), OrionGuard_ValidateFor_ToResultAsync().GetAwaiter().GetResult().AllIssues.Count, expected);
    }

    [Benchmark(Baseline = true)]
    public Task<ValidationResult> FluentValidation_ValidateAsync() => _fluentValidation.ValidateAsync(_dto);

    [Benchmark]
    public Task<GuardResult> OrionGuard_AbstractValidator_ValidateAsync() => _orionGuard.ValidateAsync(_dto);

    /// <summary>Inline API: the chain is built and run per call by design (no reusable validator object).</summary>
    [Benchmark]
    public Task<GuardResult> OrionGuard_ValidateFor_ToResultAsync() => OgInline.ValidateCustomerAsync(_dto, CancellationToken.None);
}
