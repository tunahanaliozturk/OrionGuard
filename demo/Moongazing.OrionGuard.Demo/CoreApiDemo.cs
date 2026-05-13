using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Shows the fluent <c>Ensure.That</c> API, the result-pattern accumulator,
/// conditional <c>When</c>/<c>Unless</c> validation, <c>Transform</c>/<c>Default</c>,
/// and the zero-allocation <c>FastGuard</c> entry points.
/// </summary>
public static class CoreApiDemo
{
    public static void Run()
    {
        Console.WriteLine("\n== Core Fluent API ==");

        string email = "user@example.com";
        string password = "SecureP@ss123";

        Ensure.That(email).NotNull().NotEmpty().Email();
        Ensure.That(password).NotNull().MinLength(8);
        Console.WriteLine("  Email and password validated");

        Console.WriteLine("\n== Result Pattern ==");

        var invalidEmail = "";
        var invalidPassword = "123";

        var result = GuardResult.Combine(
            Ensure.Accumulate(invalidEmail, "Email").NotNull().NotEmpty().Email().ToResult(),
            Ensure.Accumulate(invalidPassword, "Password").MinLength(8).ToResult()
        );

        if (result.IsInvalid)
        {
            Console.WriteLine($"  Validation failed with {result.Errors.Count} error(s):");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"    [{error.ParameterName}]: {error.Message}");
            }
        }

        Console.WriteLine("\n== Conditional Validation ==");

        int? age = null;
        bool isAgeRequired = false;

        var ageResult = Ensure.Accumulate(age, "Age")
            .When(isAgeRequired)
            .NotNull()
            .Always()
            .ToResult();

        Console.WriteLine($"  Age validation (optional) is valid: {ageResult.IsValid}");

        Console.WriteLine("\n== Transform and Default ==");

        string rawInput = "  Hello World  ";
        var trimmed = Ensure.That(rawInput)
            .Transform(v => v.Trim())
            .NotEmpty()
            .Build();
        Console.WriteLine($"  Transformed value: '{trimmed}'");

        string? nullable = null;
        var defaulted = Ensure.That(nullable)
            .Default("fallback-value")
            .Build();
        Console.WriteLine($"  Default applied: '{defaulted}'");

        Console.WriteLine("\n== FastGuard ==");

        FastGuard.NotNullOrEmpty("valid-string", "param");
        FastGuard.InRange(25, 1, 100, "age");
        FastGuard.Positive(42, "count");
        FastGuard.Email("test@example.com", "email");
        FastGuard.Ascii("hello123".AsSpan(), "ascii");
        FastGuard.AlphaNumeric("abc123".AsSpan(), "alphaNum");
        FastGuard.NumericString("123456".AsSpan(), "digits");
        FastGuard.MaxLength("short".AsSpan(), 100, "text");
        FastGuard.ValidGuid(Guid.NewGuid().ToString().AsSpan(), "guid");
        FastGuard.Finite(3.14, "pi");

        Console.WriteLine("  All FastGuard span-based checks passed (zero heap allocations)");
    }
}
