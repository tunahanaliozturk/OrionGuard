using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Profiles;

public static class GuardProfiles
{
    public static void Email(string value, string parameterName)
    {
        Guard.For(value, parameterName)
             .NotNull()
             .NotEmpty()
             .Matches(Utilities.GeneratedRegexPatterns.EmailPattern);
    }

    public static void Password(string value, string parameterName)
    {
        Guard.For(value, parameterName)
             .NotNull()
             .NotEmpty()
             .Length(8, 100)
             .Matches(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[@$!%*?&])");
    }

    public static void Username(string value, string parameterName)
    {
        Guard.For(value, parameterName)
             .NotNull()
             .NotEmpty()
             .Length(3, 30)
             .Matches(@"^[a-zA-Z0-9_]+\z");
    }

    public static void PhoneNumber(string value, string parameterName)
    {
        Guard.For(value, parameterName)
             .NotNull()
             .Matches(@"^\+?[1-9][0-9]{1,14}\z"); // E.164 format
    }
}
