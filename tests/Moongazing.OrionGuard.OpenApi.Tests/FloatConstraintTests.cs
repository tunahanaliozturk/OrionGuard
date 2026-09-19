using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.OpenApi.Tests;

/// <summary>
/// A <c>float</c> member compares against float literals. Widened to double, the float nearest 0.1 is
/// 0.10000000149..., so a value equal to the schema's <c>maximum</c> or to an <c>enum</c> member failed.
/// </summary>
public class FloatConstraintTests
{
    private const string Consumer = """
        using Moongazing.OrionGuard.DependencyInjection;
        namespace Sample
        {
            public sealed class Reading
            {
                public float Ratio { get; set; }
                public float Level { get; set; } = 0.5f;
            }

            [Moongazing.OrionGuard.OpenApi.OpenApiValidator("reading.json", "#/components/schemas/Reading")]
            public partial class ReadingValidator : IValidator<Reading> { }
        }
        """;

    private const string Document = """
        {
          "openapi": "3.0.3",
          "components": {
            "schemas": {
              "Reading": {
                "type": "object",
                "properties": {
                  "ratio": { "type": "number", "format": "float", "maximum": 0.1 },
                  "level": { "type": "number", "format": "float", "enum": [0.5, 0.3] }
                }
              }
            }
          }
        }
        """;

    private static readonly System.Reflection.Assembly CompiledAssembly =
        GeneratorTestHarness.Compile(Consumer, "reading.json", Document);

    private static GuardResult Validate(float ratio, float level)
    {
        dynamic reading = Activator.CreateInstance(CompiledAssembly.GetType("Sample.Reading")!)!;
        reading.Ratio = ratio;
        reading.Level = level;
        return GeneratorTestHarness.Validate(CompiledAssembly, "Sample.ReadingValidator", (object)reading);
    }

    [Fact]
    public void FloatMember_EqualToMaximumOrEnumMember_Passes()
    {
        var result = Validate(0.1f, 0.3f);

        Assert.True(
            result.IsValid,
            "Expected the bound and the enum member themselves to pass, but got: "
            + string.Join("; ", result.Errors.Select(e => $"{e.ParameterName}:{e.ErrorCode}")));
    }

    [Fact]
    public void FloatMember_AboveMaximumOrOutsideEnum_Fails()
    {
        var result = Validate(0.11f, 0.4f);

        Assert.Contains(result.Errors, e => e.ParameterName == "ratio" && e.ErrorCode == "MAXIMUM");
        Assert.Contains(result.Errors, e => e.ParameterName == "level" && e.ErrorCode == "ENUM");
    }
}
