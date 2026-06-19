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

    // v6.6 - Asynchronous validation. A rule that needs I/O (here an email-uniqueness
    // lookup) joins the same pipeline as the synchronous rules and is awaited together,
    // with the cancellation token flowing through. The terminal is idempotent: awaiting
    // the result more than once returns the same errors and runs the lookup only once.
    public Task<GuardResult> ValidateWithResultAsync(UserInput input, CancellationToken cancellationToken = default)
    {
        return Validate.For(input)
            .Property(u => u.Email, g => g.NotNull().NotEmpty().Email())
            .Property(u => u.Password, g => g.NotNull().MinLength(8))
            .Property(u => u.Username, g => g.NotNull().Length(3, 30))
            .MustAsync(u => u.Email, IsEmailAvailableAsync, "Email is already registered.", "EMAIL_TAKEN")
            .ToResultAsync(cancellationToken);
    }

    // Stands in for a database or remote lookup; returns true when the email is still free.
    private static async Task<bool> IsEmailAvailableAsync(string email, CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        return !TakenEmails.Contains(email);
    }

    private static readonly HashSet<string> TakenEmails =
        new(StringComparer.OrdinalIgnoreCase) { "taken@example.com" };
}
