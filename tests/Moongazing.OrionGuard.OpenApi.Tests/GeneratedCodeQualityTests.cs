using Microsoft.CodeAnalysis;

namespace Moongazing.OrionGuard.OpenApi.Tests;

/// <summary>
/// Guards the quality of the emitted code itself: it must implement the real OrionGuard validator
/// contract and compile with zero warnings so a consumer building under TreatWarningsAsErrors is not
/// broken by the generator.
/// </summary>
public class GeneratedCodeQualityTests
{
    private const string Consumer = """
        using System;
        using System.Collections.Generic;
        using Moongazing.OrionGuard.DependencyInjection;

        namespace Sample
        {
            public sealed class Account
            {
                public string Email { get; set; } = "";
                public string Name { get; set; } = "";
                public int Age { get; set; }
                public decimal? Limit { get; set; }
                public List<int> Scores { get; set; } = new();
            }

            [Moongazing.OrionGuard.OpenApi.OpenApiValidator("account.json", "#/components/schemas/Account")]
            public partial class AccountValidator : IValidator<Account> { }
        }
        """;

    private const string Document = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Accounts", "version": "1.0.0" },
          "components": {
            "schemas": {
              "Account": {
                "type": "object",
                "required": ["email", "name"],
                "properties": {
                  "email":  { "type": "string", "format": "email", "minLength": 5, "maxLength": 254 },
                  "name":   { "type": "string", "minLength": 1 },
                  "age":    { "type": "integer", "minimum": 0, "maximum": 150 },
                  "limit":  { "type": "number", "nullable": true, "minimum": 0 },
                  "scores": { "type": "array", "minItems": 0, "maxItems": 100 }
                }
              }
            }
          }
        }
        """;

    [Fact]
    public void GeneratedValidator_CompilesWarningClean()
    {
        var run = GeneratorTestHarness.Run(Consumer, "account.json", Document);

        var blocking = run.OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning && d.Id != "CS1701" && d.Id != "CS1702"))
            .ToList();

        Assert.True(
            blocking.Count == 0,
            "Generated code produced warnings/errors:\n"
            + string.Join("\n", blocking.Select(d => d.ToString()))
            + "\n\n--- Generated ---\n" + run.AllGeneratedText);
    }

    [Fact]
    public void GeneratedValidator_ImplementsRealValidatorContract()
    {
        var run = GeneratorTestHarness.Run(Consumer, "account.json", Document);

        // The generated partial must implement the real IValidator<T> and return the real GuardResult,
        // so it is interchangeable with a hand-written validator.
        Assert.Contains("global::Moongazing.OrionGuard.DependencyInjection.IValidator<global::Sample.Account>", run.AllGeneratedText);
        Assert.Contains("global::Moongazing.OrionGuard.Core.GuardResult Validate(global::Sample.Account value)", run.AllGeneratedText);
        Assert.Contains("ValidateAsync(global::Sample.Account value", run.AllGeneratedText);
    }

    [Fact]
    public void GeneratedValidator_IsDeterministic()
    {
        var first = GeneratorTestHarness.Run(Consumer, "account.json", Document).AllGeneratedText;
        var second = GeneratorTestHarness.Run(Consumer, "account.json", Document).AllGeneratedText;

        Assert.Equal(first, second);
    }
}
