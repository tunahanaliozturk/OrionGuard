namespace Moongazing.OrionGuard.Generators.Tests;

using System.Globalization;
using static GeneratorTestHarness;

/// <summary>
/// <c>[Range]</c> bounds are emitted as C# literals, so they must be culture-invariant and typed to the
/// property: the generator used to print them with the build machine's culture and as bare doubles.
/// </summary>
public class GenerateValidatorRangeTests
{
    [Fact]
    public void GeneratedValidator_ShouldCompileAndEnforceRange_WhenBuiltUnderACommaDecimalCulture()
    {
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                [GenerateValidator]
                public sealed class Probe
                {
                    [Range(0.5, 99.9)] public double Ratio { get; set; }
                }
            }
            """;

        // tr-TR formats 0.5 as "0,5", which emitted "Ratio < 0,5" (CS1026). The generator reads the
        // current culture of the thread that runs it, which is this test's thread.
        var original = CultureInfo.CurrentCulture;
        System.Reflection.Assembly assembly;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            assembly = Compile(source, new OrionGuardGenerator());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        var tooSmall = Validate(assembly, "App.ProbeValidator", New(assembly, "App.Probe", ("Ratio", 0.4)));
        var atBound = Validate(assembly, "App.ProbeValidator", New(assembly, "App.Probe", ("Ratio", 0.5)));

        Assert.Contains(tooSmall.Errors, e => e.ParameterName == "Ratio" && e.ErrorCode == "RANGE");
        Assert.Contains("0.5 and 99.9", tooSmall.Errors.Single().Message);
        Assert.True(atBound.IsValid, ErrorSummary(atBound));
    }

    [Fact]
    public void GeneratedValidator_ShouldCompareInDecimal_ForDecimalProperties()
    {
        // A bare double bound against a decimal does not compile (CS0019). double.MaxValue has no
        // decimal literal at all, so that bound must still compile.
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                [GenerateValidator]
                public sealed class Money
                {
                    [Range(0.01, 1000)] public decimal Price { get; set; } = 1m;
                    [Range(0.5, 2.5)] public decimal? Discount { get; set; }
                    [Range(0, double.MaxValue)] public decimal Total { get; set; }
                }
            }
            """;

        var assembly = Compile(source, new OrionGuardGenerator());

        var valid = Validate(assembly, "App.MoneyValidator",
            New(assembly, "App.Money", ("Price", 0.01m), ("Discount", null), ("Total", 5m)));
        var invalid = Validate(assembly, "App.MoneyValidator",
            New(assembly, "App.Money", ("Price", 0.009m), ("Discount", 3m), ("Total", -1m)));

        Assert.True(valid.IsValid, ErrorSummary(valid));
        Assert.Equal(new[] { "Price", "Discount", "Total" }, invalid.Errors.Select(e => e.ParameterName));
    }

    [Fact]
    public void GeneratedValidator_ShouldAcceptTheBoundItself_ForFloatProperties()
    {
        // Compared in double, the float 0.1f (0.10000000149...) is greater than the bound 0.1 and was
        // rejected. The bound is now a float literal.
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                [GenerateValidator]
                public sealed class Sample
                {
                    [Range(0, 0.1)] public float Weight { get; set; }
                }
            }
            """;

        var assembly = Compile(source, new OrionGuardGenerator());

        var atBound = Validate(assembly, "App.SampleValidator", New(assembly, "App.Sample", ("Weight", 0.1f)));
        var over = Validate(assembly, "App.SampleValidator", New(assembly, "App.Sample", ("Weight", 0.11f)));

        Assert.True(atBound.IsValid, ErrorSummary(atBound));
        Assert.Contains(over.Errors, e => e.ParameterName == "Weight" && e.ErrorCode == "RANGE");
    }
    [Fact]
    public void GeneratedValidator_ShouldRejectEveryValue_WhenARangeBoundIsNaN()
    {
        // Every comparison against NaN is false, so the reflection-based RangeAttribute rejects the value.
        // The generated check compared against NaN too, which did the opposite and let the value through.
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                [GenerateValidator]
                public sealed class Sample
                {
                    [Range(double.NaN, 10)] public double Weight { get; set; }
                    [Range(0, double.NaN)] public double? Optional { get; set; }
                }
            }
            """;

        var assembly = Compile(source, new OrionGuardGenerator());

        var inRange = Validate(assembly, "App.SampleValidator", New(assembly, "App.Sample", ("Weight", 5d), ("Optional", 5d)));
        var missing = Validate(assembly, "App.SampleValidator", New(assembly, "App.Sample", ("Weight", 5d)));

        Assert.Contains(inRange.Errors, e => e.ParameterName == "Weight" && e.ErrorCode == "RANGE");
        Assert.Contains(inRange.Errors, e => e.ParameterName == "Optional" && e.ErrorCode == "RANGE");
        // A null value is outside every rule's reach, NaN bound or not.
        Assert.DoesNotContain(missing.Errors, e => e.ParameterName == "Optional");
    }
}
