using Moongazing.OrionGuard.Demo.Models;
using Moongazing.OrionGuard.Demo.Profiles;
using Moongazing.OrionGuard.Demo.Services;
using Moongazing.OrionGuard.Profiles;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Extensions;
using Moongazing.OrionGuard.Localization;

Console.WriteLine("🔐 OrionGuard v4.0 - Full Demo\n");
Console.WriteLine("=".PadRight(50, '='));

#region 🌟 NEW v4.0 Features

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 1. FLUENT API with Ensure.That()
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 1. Fluent API - Ensure.That()");

string email = "user@example.com";
string password = "SecureP@ss123";

// Automatic parameter name capture with CallerArgumentExpression
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(password).NotNull().MinLength(8);
Console.WriteLine("   ✅ Email and password validated!");

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 2. RESULT PATTERN - Error Accumulation
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 2. Result Pattern - Accumulate Errors");

var invalidEmail = "";
var invalidPassword = "123";

var result = GuardResult.Combine(
    Ensure.Accumulate(invalidEmail, "Email").NotNull().NotEmpty().Email().ToResult(),
    Ensure.Accumulate(invalidPassword, "Password").MinLength(8).ToResult()
);

if (result.IsInvalid)
{
    Console.WriteLine($"   ⚠️ Validation failed with {result.Errors.Count} error(s):");
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"      - [{error.ParameterName}]: {error.Message}");
    }
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 3. CONDITIONAL VALIDATION - When/Unless
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 3. Conditional Validation - When/Unless");

int? age = null;
bool isAgeRequired = false;

// Only validate when required
var ageResult = Ensure.Accumulate(age, "Age")
    .When(isAgeRequired)
    .NotNull()
    .Always() // Reset condition
    .ToResult();

Console.WriteLine($"   ✅ Age validation (optional): {(ageResult.IsValid ? "Passed" : "Failed")}");

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 4. OBJECT VALIDATION
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 4. Object Validation");

var userInput = new UserInput
{
    Email = "test@example.com",
    Password = "Test123$Secure",
    Username = "user_01"
};

var objectResult = Validate.Object(userInput)
    .Property(u => u.Email, g => g.NotNull().NotEmpty().Email())
    .Property(u => u.Password, g => g.NotNull().MinLength(8))
    .Property(u => u.Username, g => g.NotNull().Length(3, 30))
    .ToResult();

Console.WriteLine($"   ✅ Object validation: {(objectResult.IsValid ? "Passed" : "Failed")}");

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 5. COMMON PROFILES
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 5. Built-in Profiles");

var emailResult = CommonProfiles.Email("valid@email.com");
var passwordResult = CommonProfiles.Password("StrongP@ss1", minLength: 8);
var usernameResult = CommonProfiles.Username("john_doe");

Console.WriteLine($"   ✅ Email: {(emailResult.IsValid ? "Valid" : "Invalid")}");
Console.WriteLine($"   ✅ Password: {(passwordResult.IsValid ? "Valid" : "Invalid")}");
Console.WriteLine($"   ✅ Username: {(usernameResult.IsValid ? "Valid" : "Invalid")}");

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 6. ADVANCED VALIDATORS
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 6. Advanced Validators");

try
{
    // Credit Card (Luhn algorithm)
    "4111111111111111".AgainstInvalidCreditCard("creditCard");
    Console.WriteLine("   ✅ Credit card validated!");

    // IBAN
    "DE89370400440532013000".AgainstInvalidIban("iban");
    Console.WriteLine("   ✅ IBAN validated!");

    // JSON
    "{\"name\": \"test\"}".AgainstInvalidJson("jsonData");
    Console.WriteLine("   ✅ JSON validated!");

    // Turkish ID (TC Kimlik No)
    "10000000146".AgainstInvalidTurkishId("tcKimlik");
    Console.WriteLine("   ✅ Turkish ID validated!");

    // Base64
    "SGVsbG8gV29ybGQ=".AgainstInvalidBase64("base64Data");
    Console.WriteLine("   ✅ Base64 validated!");

    // Hex Color
    "#FF5733".AgainstInvalidHexColor("color");
    Console.WriteLine("   ✅ Hex color validated!");
}
catch (ArgumentException ex)
{
    Console.WriteLine($"   ❌ Validation error: {ex.Message}");
}

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 7. LOCALIZATION
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 7. Localization Support");

ValidationMessages.SetCulture("tr");
Console.WriteLine($"   TR: {ValidationMessages.Get("NotNull", "Email")}");

ValidationMessages.SetCulture("en");
Console.WriteLine($"   EN: {ValidationMessages.Get("NotNull", "Email")}");

ValidationMessages.SetCulture("de");
Console.WriteLine($"   DE: {ValidationMessages.Get("NotNull", "Email")}");

#endregion

Console.WriteLine("\n" + "=".PadRight(50, '='));

#region 🎯 Legacy API (Still works!)

Console.WriteLine("\n📌 Legacy API - Still Compatible!");

// Guard: Null & Empty
Guard.AgainstNull("test", "testParam");
Guard.AgainstNullOrEmpty("some text", "textParam");

// Guard: Range
Guard.AgainstOutOfRange(25, 18, 60, "age");

// Guard.For fluent builder (v3.0)
Guard.For("valid@email.com", "email")
     .NotNull()
     .NotEmpty()
     .Matches(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

Console.WriteLine("   ✅ Legacy guards still work!");

#endregion

#region 🎯 Real-world usage

Console.WriteLine("\n📌 Real-world Service Example");

GuardProfileRegistry.Register<string>("SafeUsername", CustomProfiles.SafeUsername);

var input = new UserInput
{
    Email = "test@example.com",
    Password = "Test123$Secure",
    Username = "user_01"
};

var registrationService = new RegistrationService();
registrationService.Register(input);
Console.WriteLine("   ✅ Registration succeeded!");

#endregion

Console.WriteLine("\n" + "=".PadRight(50, '='));
Console.WriteLine("🎉 All demos completed successfully!");
Console.WriteLine("=".PadRight(50, '='));

