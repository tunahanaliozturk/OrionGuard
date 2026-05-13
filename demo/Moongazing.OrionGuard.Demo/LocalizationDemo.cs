using Moongazing.OrionGuard.Localization;
using System.Globalization;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Walks the bundled translation catalogue, including the scoped-culture API
/// and the v6.1 DDD keys that ship in 14 languages.
/// </summary>
public static class LocalizationDemo
{
    public static void Run()
    {
        Console.WriteLine("\n== Localization (9 languages) ==");

        string[] languages = ["en", "tr", "de", "fr", "es", "pt", "ar", "ja", "it"];
        foreach (var lang in languages)
        {
            var msg = ValidationMessages.Get("NotNull", new CultureInfo(lang), "Email");
            Console.WriteLine($"  {lang.ToUpperInvariant()}: {msg}");
        }

        Console.WriteLine("  Thread-safe scoped culture:");
        ValidationMessages.SetCultureForCurrentScope(new CultureInfo("tr"));
        Console.WriteLine($"  Scoped (TR): {ValidationMessages.Get("NotNull", "Field")}");

        Console.WriteLine("\n== DDD Localization Keys (14 languages) ==");

        ValidationMessages.SetCultureForCurrentScope(CultureInfo.InvariantCulture);

        string[] sampleLangs = ["en", "tr", "de", "ja", "ar"];
        foreach (var lang in sampleLangs)
        {
            var ci = new CultureInfo(lang);
            var msg = ValidationMessages.Get("DefaultStronglyTypedId", ci, "OrderId");
            Console.WriteLine($"  {lang.ToUpperInvariant()}: {msg}");
        }

        Console.WriteLine("  Two more keys also ship in all 14 languages: BusinessRuleBroken, DomainInvariantViolated");
    }
}
