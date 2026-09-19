using BenchmarkDotNet.Attributes;
using FluentValidation.Results;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Benchmarks.Comparison;

/// <summary>
/// Scenario (a): a 5-property DTO with one rule per property (NotEmpty, MaximumLength, EmailAddress,
/// InclusiveBetween, Must), for valid input and for input that fails all 5 rules.
/// FluentValidation is the baseline, so Ratio reads "OrionGuard time / FluentValidation time".
/// </summary>
[MemoryDiagnoser]
public class SmallDtoComparisonBenchmarks
{
    private FvCustomerValidator _fluentValidation = null!;
    private OgCustomerValidator _orionGuard = null!;
    private OgCompatCustomerValidator _orionGuardCompat = null!;
    private CustomerDto _dto = null!;

    [Params("Valid", "Invalid")]
    public string Input { get; set; } = "Valid";

    [GlobalSetup]
    public void Setup()
    {
        _fluentValidation = new FvCustomerValidator();
        _orionGuard = new OgCustomerValidator();
        _orionGuardCompat = new OgCompatCustomerValidator();
        _dto = Input == "Valid" ? CustomerDto.Valid() : CustomerDto.Invalid();

        var expected = Input == "Valid" ? 0 : 5;
        Parity.Expect(nameof(FluentValidation_AbstractValidator), FluentValidation_AbstractValidator().Errors.Count, expected);
        Parity.Expect(nameof(OrionGuard_AbstractValidator), OrionGuard_AbstractValidator().AllIssues.Count, expected);
        Parity.Expect(nameof(OrionGuard_FluentStyleValidator), OrionGuard_FluentStyleValidator().AllIssues.Count, expected);
        Parity.Expect(nameof(OrionGuard_ValidateFor), OrionGuard_ValidateFor().AllIssues.Count, expected);
        Parity.Expect(nameof(OrionGuard_SourceGenerated), OrionGuard_SourceGenerated().AllIssues.Count, expected);
    }

    [Benchmark(Baseline = true)]
    public ValidationResult FluentValidation_AbstractValidator() => _fluentValidation.Validate(_dto);

    [Benchmark]
    public GuardResult OrionGuard_AbstractValidator() => _orionGuard.Validate(_dto);

    /// <summary>The FluentValidation-compatible base class a migrating user starts with.</summary>
    [Benchmark]
    public GuardResult OrionGuard_FluentStyleValidator() => _orionGuardCompat.Validate(_dto);

    /// <summary>Inline API: the chain is built and run per call by design (no reusable validator object).</summary>
    [Benchmark]
    public GuardResult OrionGuard_ValidateFor() => OgInline.ValidateCustomer(_dto);

    /// <summary>Different programming model: attributes + [GenerateValidator], plus the Must rule by hand.</summary>
    [Benchmark]
    public GuardResult OrionGuard_SourceGenerated() => OgInline.ValidateCustomerGenerated(_dto);
}
