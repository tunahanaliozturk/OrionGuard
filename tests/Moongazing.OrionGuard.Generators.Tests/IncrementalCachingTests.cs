namespace Moongazing.OrionGuard.Generators.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Generators.StronglyTypedIds;

/// <summary>
/// An edit that does not touch a target must leave the generated output cached. The transform re-runs
/// on every compilation change, so the pipeline only caches when the models it returns compare equal
/// by value; reference-equal models regenerated every file on every keystroke in the IDE.
/// </summary>
public class IncrementalCachingTests
{
    [Theory]
    [InlineData("validator")]
    [InlineData("stronglyTypedId")]
    public void Generator_ShouldReuseCachedOutput_AfterAnUnrelatedEdit(string generatorKind)
    {
        const string source = """
            using Moongazing.OrionGuard.Attributes;
            using Moongazing.OrionGuard.Domain.Primitives;
            using Moongazing.OrionGuard.Generators;

            namespace App
            {
                [GenerateValidator]
                public sealed class Signup
                {
                    [NotNull, Length(3, 50)] public string? Name { get; set; }
                    [Range(0.5, 10)] public decimal Score { get; set; }
                }

                [StronglyTypedId<System.Guid>] public readonly partial struct SignupId { }
            }
            """;

        IIncrementalGenerator generator = generatorKind == "validator"
            ? new OrionGuardGenerator()
            : new StronglyTypedIdGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        var compilation = GeneratorTestHarness.CreateCompilation(source);
        driver = driver.RunGenerators(compilation);

        var edited = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace App { internal static class Unrelated { } }"));
        driver = driver.RunGenerators(edited);

        var outputs = driver.GetRunResult().Results.Single().TrackedOutputSteps
            .SelectMany(step => step.Value)
            .SelectMany(run => run.Outputs)
            .ToList();

        Assert.NotEmpty(outputs);
        Assert.All(outputs, output => Assert.True(
            output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Output step re-ran with reason {output.Reason}."));
    }
}
