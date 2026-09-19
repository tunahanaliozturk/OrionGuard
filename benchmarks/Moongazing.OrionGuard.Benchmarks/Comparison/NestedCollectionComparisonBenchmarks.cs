using BenchmarkDotNet.Attributes;
using FluentValidation.Results;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Benchmarks.Comparison;

/// <summary>
/// Scenario (b): an order with a nested address (2 rules) and a collection of 10 lines (2 rules each),
/// plus 1 root rule. Invalid input fails every rule: 1 + 2 + 10 x 2 = 23 errors.
/// FluentValidation uses SetValidator / RuleForEach; OrionGuard uses Validate.Nested, its only API with
/// nested-object and collection support (FluentStyleValidator and [GenerateValidator] have none).
/// </summary>
[MemoryDiagnoser]
public class NestedCollectionComparisonBenchmarks
{
    private const int LineCount = 10;

    private FvOrderValidator _fluentValidation = null!;
    private OrderDto _order = null!;

    [Params("Valid", "Invalid")]
    public string Input { get; set; } = "Valid";

    [GlobalSetup]
    public void Setup()
    {
        _fluentValidation = new FvOrderValidator();
        _order = OrderDto.Create(Input == "Valid", LineCount);

        var expected = Input == "Valid" ? 0 : 1 + 2 + (LineCount * 2);
        Parity.Expect(nameof(FluentValidation_SetValidator_RuleForEach), FluentValidation_SetValidator_RuleForEach().Errors.Count, expected);
        Parity.Expect(nameof(OrionGuard_ValidateNested), OrionGuard_ValidateNested().AllIssues.Count, expected);
    }

    [Benchmark(Baseline = true)]
    public ValidationResult FluentValidation_SetValidator_RuleForEach() => _fluentValidation.Validate(_order);

    /// <summary>Inline API: the chain is built and run per call by design (no reusable validator object).</summary>
    [Benchmark]
    public GuardResult OrionGuard_ValidateNested() => OgInline.ValidateOrder(_order);
}
