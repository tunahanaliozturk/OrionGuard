using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Demo.Models;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Shows object-level validation with <c>Validate.For(...)</c>, including
/// cross-property rules and the <c>When</c> conditional block.
/// </summary>
public static class ObjectValidationDemo
{
    public static void Run()
    {
        Console.WriteLine("\n== Object Validation ==");

        var userInput = new UserInput
        {
            Email = "test@example.com",
            Password = "Test123$Secure",
            Username = "user_01"
        };

        var objectResult = Validate.For(userInput)
            .Property(u => u.Email, g => g.NotNull().NotEmpty().Email())
            .Property(u => u.Password, g => g.NotNull().MinLength(8))
            .Property(u => u.Username, g => g.NotNull().Length(3, 30))
            .CrossProperty(u => u.Email, u => u.Username,
                (e, u) => e != u, "Email and Username must be different")
            .ToResult();

        Console.WriteLine($"  Object validation is valid: {objectResult.IsValid}");

        var conditionalResult = Validate.For(userInput)
            .When(userInput.Email.Contains('@'), v => v
                .Property(u => u.Email, g => g.Email()))
            .ToResult();

        Console.WriteLine($"  Conditional validation is valid: {conditionalResult.IsValid}");
    }
}
