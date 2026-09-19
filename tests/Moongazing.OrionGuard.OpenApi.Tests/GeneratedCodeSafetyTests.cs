using System.Reflection;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.OpenApi.Tests;

/// <summary>
/// Hostile input must neither take down the compiler nor get past, or hang, the generated validator:
/// a deeply nested document is a parse diagnostic, not a stack overflow; a catastrophically
/// backtracking <c>pattern</c> times out into a validation error; and format checks anchor at the true
/// end of the value.
/// </summary>
public class GeneratedCodeSafetyTests
{
    private const string Consumer = """
        using Moongazing.OrionGuard.DependencyInjection;
        namespace Sample
        {
            public sealed class Profile
            {
                public string Slug { get; set; } = "aaaa";
                public string Date { get; set; } = "2026-06-23";
                public string Stamp { get; set; } = "2026-06-23T10:30:00Z";
                public string Id { get; set; } = "12345678-1234-1234-1234-123456789abc";
                public string Host { get; set; } = "example.com";
                public string Ip { get; set; } = "10.0.0.1";
                public string Site { get; set; } = "https://example.com";
            }

            [Moongazing.OrionGuard.OpenApi.OpenApiValidator("profile.json", "#/components/schemas/Profile")]
            public partial class ProfileValidator : IValidator<Profile> { }
        }
        """;

    private const string Document = """
        {
          "openapi": "3.0.3",
          "components": {
            "schemas": {
              "Profile": {
                "type": "object",
                "properties": {
                  "slug":  { "type": "string", "pattern": "^(a|aa)+$" },
                  "date":  { "type": "string", "format": "date" },
                  "stamp": { "type": "string", "format": "date-time" },
                  "id":    { "type": "string", "format": "uuid" },
                  "host":  { "type": "string", "format": "hostname" },
                  "ip":    { "type": "string", "format": "ipv4" },
                  "site":  { "type": "string", "format": "uri" }
                }
              }
            }
          }
        }
        """;

    private static readonly Assembly CompiledAssembly =
        GeneratorTestHarness.Compile(Consumer, "profile.json", Document);

    private static GuardResult Validate(string? property = null, string? value = null)
    {
        var type = CompiledAssembly.GetType("Sample.Profile")!;
        var profile = Activator.CreateInstance(type)!;
        if (property is not null)
        {
            type.GetProperty(property)!.SetValue(profile, value);
        }

        return GeneratorTestHarness.Validate(CompiledAssembly, "Sample.ProfileValidator", profile);
    }

    [Fact]
    public void ValidFormats_Pass()
    {
        var result = Validate();

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.ParameterName}:{e.ErrorCode}")));
    }

    [Theory]
    [InlineData("Date", "2026-06-23\n")]
    [InlineData("Stamp", "2026-06-23T10:30:00Z\n")]
    [InlineData("Id", "12345678-1234-1234-1234-123456789abc\n")]
    [InlineData("Host", "example.com\n")]
    [InlineData("Ip", "10.0.0.1\n")]
    [InlineData("Site", "https://example.com\n")]
    [InlineData("Date", "٢٠٢٦-٠٦-٢٣")]
    public void Format_WithTrailingNewlineOrNonAsciiDigits_Fails(string property, string value)
    {
        // '$' also matches before a final newline, and \d matches any Unicode digit (here Arabic-Indic).
        var result = Validate(property, value);

        Assert.Contains(result.Errors, e => e.ParameterName == property.ToLowerInvariant() && e.ErrorCode == "FORMAT");
    }

    [Fact]
    public async Task Pattern_ThatBacktracksCatastrophically_TimesOutIntoAValidationError()
    {
        // The static Regex.IsMatch the validator used to call has no timeout: this input would run for
        // hours. The bounded wait turns that into a test failure instead of a hung test run.
        var validation = Task.Run(() => Validate("Slug", new string('a', 64) + "!"));

        var finished = await Task.WhenAny(validation, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.True(finished == validation, "Validation did not finish: the pattern check has no timeout.");
        Assert.Contains((await validation).Errors, e => e.ParameterName == "slug" && e.ErrorCode == "PATTERN");
    }

    [Theory]
    [InlineData(300)]
    [InlineData(100_000)]
    public void DeeplyNestedDocument_ReportsOG1002_AndDoesNotOverflowTheStack(int depth)
    {
        // The recursive-descent parser had no depth limit: 100,000 nested arrays overflowed the stack,
        // which cannot be caught and crashes the compiler server and the IDE.
        string nested = new string('[', depth) + new string(']', depth);
        string document = "{ \"x-deep\": " + nested + ", " + Document.TrimStart().Substring(1);

        var run = GeneratorTestHarness.Run(Consumer, "profile.json", document);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics, d => d.Id == "OG1002");
        Assert.Contains("maximum depth of 256", diagnostic.GetMessage());
        Assert.Equal(0, run.ValidatorSourceCount);
    }
}
