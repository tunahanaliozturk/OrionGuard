using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Shows the injection/attack-shape guards, the universal format guards
/// (geo, MAC, hostname, CIDR, JWT, ...) and the advanced format validators
/// (credit card, IBAN, JSON, Turkish ID, Base64, hex colour).
/// </summary>
public static class SecurityGuardsDemo
{
    public static void Run()
    {
        Console.WriteLine("\n== Security Guards ==");

        var safeInput = "normal user input";
        safeInput.AgainstSqlInjection("input");
        safeInput.AgainstXss("input");
        safeInput.AgainstPathTraversal("input");
        safeInput.AgainstCommandInjection("input");
        safeInput.AgainstLdapInjection("input");
        Console.WriteLine("  SQL injection check passed");
        Console.WriteLine("  XSS check passed");
        Console.WriteLine("  Path traversal check passed");
        Console.WriteLine("  Command injection check passed");
        Console.WriteLine("  LDAP injection check passed");

        "safe-filename.pdf".AgainstUnsafeFileName("filename");
        Console.WriteLine("  Safe filename accepted");

        try { "SELECT * FROM Users".AgainstSqlInjection("search"); }
        catch (ArgumentException ex) { Console.WriteLine($"  Blocked SQL injection: {ex.Message}"); }

        try { "<script>alert('xss')</script>".AgainstXss("comment"); }
        catch (ArgumentException ex) { Console.WriteLine($"  Blocked XSS: {ex.Message}"); }

        try { "../../etc/passwd".AgainstPathTraversal("path"); }
        catch (ArgumentException ex) { Console.WriteLine($"  Blocked path traversal: {ex.Message}"); }

        Console.WriteLine("\n== Format Guards ==");

        try
        {
            41.0082.AgainstInvalidLatitude("latitude");
            28.9784.AgainstInvalidLongitude("longitude");
            Console.WriteLine("  Geo coordinates (41.0082, 28.9784) accepted");

            "00:1A:2B:3C:4D:5E".AgainstInvalidMacAddress("mac");
            Console.WriteLine("  MAC address parsed");

            "api.example.com".AgainstInvalidHostname("hostname");
            Console.WriteLine("  Hostname accepted");

            "192.168.1.0/24".AgainstInvalidCidr("cidr");
            Console.WriteLine("  CIDR notation accepted");

            "US".AgainstInvalidCountryCode("country");
            Console.WriteLine("  ISO country code accepted");

            "Europe/Istanbul".AgainstInvalidTimeZoneId("timezone");
            Console.WriteLine("  Time zone id accepted");

            "en-US".AgainstInvalidLanguageTag("lang");
            Console.WriteLine("  Language tag accepted");

            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abc123".AgainstInvalidJwtFormat("token");
            Console.WriteLine("  JWT structure valid");

            "Server=localhost;Database=mydb;User=sa;Password=secret".AgainstInvalidConnectionString("connStr");
            Console.WriteLine("  Connection string accepted");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  Format validation error: {ex.Message}");
        }

        Console.WriteLine("\n== Advanced Validators ==");

        try
        {
            "4111111111111111".AgainstInvalidCreditCard("creditCard");
            Console.WriteLine("  Credit card accepted");

            "DE89370400440532013000".AgainstInvalidIban("iban");
            Console.WriteLine("  IBAN accepted");

            "{\"name\": \"test\"}".AgainstInvalidJson("jsonData");
            Console.WriteLine("  JSON parsed");

            "10000000146".AgainstInvalidTurkishId("tcKimlik");
            Console.WriteLine("  Turkish national id accepted");

            "SGVsbG8gV29ybGQ=".AgainstInvalidBase64("base64Data");
            Console.WriteLine("  Base64 accepted");

            "#FF5733".AgainstInvalidHexColor("color");
            Console.WriteLine("  Hex colour accepted");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"  Validation error: {ex.Message}");
        }
    }
}
