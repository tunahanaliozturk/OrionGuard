using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Demo.Models;
using Moongazing.OrionGuard.Profiles;

namespace Moongazing.OrionGuard.Demo.Services;

public class RegistrationService
{
    public void Register(UserInput input)
    {
        // v4.0 Fluent Validation with Ensure
        Ensure.That(input.Email).NotNull().NotEmpty().Email();
        Ensure.That(input.Password).NotNull().MinLength(8);

        // Legacy Guard.For still works
        Guard.For(input.Username, nameof(input.Username))
             .NotNull()
             .NotEmpty()
             .Length(3, 30);

        // Profile Registry (custom demo)
        GuardProfileRegistry.Execute("SafeUsername", input.Username, nameof(input.Username));
    }

    // v4.0 - Object validation approach
    public GuardResult ValidateWithResult(UserInput input)
    {
        return Validate.For(input)
            .Property(u => u.Email, g => g.NotNull().NotEmpty().Email())
            .Property(u => u.Password, g => g.NotNull().MinLength(8))
            .Property(u => u.Username, g => g.NotNull().Length(3, 30))
            .ToResult();
    }
}
