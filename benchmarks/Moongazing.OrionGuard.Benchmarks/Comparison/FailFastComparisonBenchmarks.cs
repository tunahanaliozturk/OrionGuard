using BenchmarkDotNet.Attributes;
using FluentValidation;
using FluentValidation.Results;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Benchmarks.Comparison;

/// <summary>
/// Scenario (c): collecting all errors vs failing fast, on input that fails all 5 rules of scenario (a).
/// FluentValidation fails fast with ClassLevelCascadeMode.Stop and can return a result or throw.
/// OrionGuard's fail-fast primitive is Validate.ForStrict, which always throws, so the throwing
/// FluentValidation variant (ValidateAndThrow) is included for a like-for-like row.
/// </summary>
[MemoryDiagnoser]
public class FailFastComparisonBenchmarks
{
    private FvCustomerValidator _collectAll = null!;
    private FvCustomerStopValidator _stopOnFirstFailure = null!;
    private CustomerDto _dto = null!;

    [GlobalSetup]
    public void Setup()
    {
        _collectAll = new FvCustomerValidator();
        _stopOnFirstFailure = new FvCustomerStopValidator();
        _dto = CustomerDto.Invalid();

        Parity.Expect(nameof(FluentValidation_CollectAll), FluentValidation_CollectAll().Errors.Count, 5);
        Parity.Expect(nameof(FluentValidation_StopOnFirstFailure), FluentValidation_StopOnFirstFailure().Errors.Count, 1);
        Parity.Expect(nameof(FluentValidation_StopOnFirstFailure_Throw), FluentValidation_StopOnFirstFailure_Throw().Errors.Count(), 1);
        Parity.Expect(nameof(OrionGuard_ValidateFor_CollectAll), OrionGuard_ValidateFor_CollectAll().AllIssues.Count, 5);
        Parity.Expect(nameof(OrionGuard_ValidateForStrict_Throw), OrionGuard_ValidateForStrict_Throw().Errors.Count, 1);
    }

    [Benchmark(Baseline = true)]
    public ValidationResult FluentValidation_CollectAll() => _collectAll.Validate(_dto);

    [Benchmark]
    public ValidationResult FluentValidation_StopOnFirstFailure() => _stopOnFirstFailure.Validate(_dto);

    [Benchmark]
    public ValidationException FluentValidation_StopOnFirstFailure_Throw()
    {
        try
        {
            _stopOnFirstFailure.ValidateAndThrow(_dto);
        }
        catch (ValidationException ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Expected a validation failure.");
    }

    [Benchmark]
    public GuardResult OrionGuard_ValidateFor_CollectAll() => OgInline.ValidateCustomer(_dto);

    [Benchmark]
    public AggregateValidationException OrionGuard_ValidateForStrict_Throw()
    {
        try
        {
            OgInline.ValidateCustomerStrict(_dto);
        }
        catch (AggregateValidationException ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Expected a validation failure.");
    }
}
