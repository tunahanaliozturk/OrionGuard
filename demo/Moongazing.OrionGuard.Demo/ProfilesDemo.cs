using Moongazing.OrionGuard.Demo.Models;
using Moongazing.OrionGuard.Demo.Profiles;
using Moongazing.OrionGuard.Demo.Services;
using Moongazing.OrionGuard.Profiles;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Shows the built-in <c>CommonProfiles</c> (email, password, username) and a
/// real-world service example that registers a custom profile and runs a
/// registration through it.
/// </summary>
public static class ProfilesDemo
{
    public static void Run()
    {
        Console.WriteLine("\n== Built-in Profiles ==");

        var emailResult = CommonProfiles.Email("valid@email.com");
        var passwordResult = CommonProfiles.Password("StrongP@ss1", minLength: 8);
        var usernameResult = CommonProfiles.Username("john_doe");

        Console.WriteLine($"  Email profile valid: {emailResult.IsValid}");
        Console.WriteLine($"  Password profile valid: {passwordResult.IsValid}");
        Console.WriteLine($"  Username profile valid: {usernameResult.IsValid}");

        Console.WriteLine("\n== Real-world Service Example ==");

        GuardProfileRegistry.Register<string>("SafeUsername", CustomProfiles.SafeUsername);

        var input = new UserInput
        {
            Email = "test@example.com",
            Password = "Test123$Secure",
            Username = "user_01"
        };

        var registrationService = new RegistrationService();
        registrationService.Register(input);
        Console.WriteLine("  Registration succeeded");
    }
}
