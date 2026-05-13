using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Confirms that the pre-fluent <c>Guard.AgainstNull</c>/<c>Guard.For</c> API
/// from earlier versions still works alongside the new fluent surface.
/// </summary>
public static class LegacyApiDemo
{
    public static void Run()
    {
        Console.WriteLine("\n== Legacy API (backward compatible) ==");

        Guard.AgainstNull("test", "testParam");
        Guard.AgainstNullOrEmpty("some text", "textParam");
        Guard.AgainstOutOfRange(25, 18, 60, "age");

        Guard.For("valid@email.com", "email")
             .NotNull()
             .NotEmpty()
             .Matches(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

        Console.WriteLine("  Legacy guards still work");
    }
}
