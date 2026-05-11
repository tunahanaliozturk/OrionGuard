using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Demo.Domain;
using Moongazing.OrionGuard.Demo.Models;
using Moongazing.OrionGuard.Demo.Profiles;
using Moongazing.OrionGuard.Demo.Services;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Exceptions;
using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Extensions;
using Moongazing.OrionGuard.Localization;
using Moongazing.OrionGuard.Profiles;
using System.Globalization;

Console.WriteLine("🔐 OrionGuard v6.1 - Full Demo (DDD Primitives + all v5/v6.0 features)\n");
Console.WriteLine("=".PadRight(60, '='));

#region 🌟 Core Fluent API

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 1. FLUENT API with Ensure.That()
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 1. Fluent API - Ensure.That()");

string email = "user@example.com";
string password = "SecureP@ss123";

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

var ageResult = Ensure.Accumulate(age, "Age")
    .When(isAgeRequired)
    .NotNull()
    .Always()
    .ToResult();

Console.WriteLine($"   ✅ Age validation (optional): {(ageResult.IsValid ? "Passed" : "Failed")}");

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 4. TRANSFORM & DEFAULT
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 4. Transform & Default (NEW v5.0)");

string rawInput = "  Hello World  ";
var trimmed = Ensure.That(rawInput)
    .Transform(v => v.Trim())
    .NotEmpty()
    .Build();
Console.WriteLine($"   ✅ Transformed: '{trimmed}'");

string? nullable = null;
var defaulted = Ensure.That(nullable)
    .Default("fallback-value")
    .Build();
Console.WriteLine($"   ✅ Default applied: '{defaulted}'");

#endregion

#region 🌟 Object Validation

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 5. OBJECT VALIDATION with CrossProperty
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 5. Object Validation + Cross-Property (NEW v5.0)");

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

Console.WriteLine($"   ✅ Object validation: {(objectResult.IsValid ? "Passed" : "Failed")}");

// Conditional object validation with When
var conditionalResult = Validate.For(userInput)
    .When(userInput.Email.Contains('@'), v => v
        .Property(u => u.Email, g => g.Email()))
    .ToResult();

Console.WriteLine($"   ✅ Conditional validation: {(conditionalResult.IsValid ? "Passed" : "Failed")}");

#endregion

#region 🌟 Security Guards

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 6. SECURITY GUARDS (NEW v5.0)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 6. Security Guards (NEW v5.0)");

var safeInput = "normal user input";
safeInput.AgainstSqlInjection("input");
safeInput.AgainstXss("input");
safeInput.AgainstPathTraversal("input");
safeInput.AgainstCommandInjection("input");
safeInput.AgainstLdapInjection("input");
Console.WriteLine("   ✅ SQL Injection check passed!");
Console.WriteLine("   ✅ XSS check passed!");
Console.WriteLine("   ✅ Path Traversal check passed!");
Console.WriteLine("   ✅ Command Injection check passed!");
Console.WriteLine("   ✅ LDAP Injection check passed!");

// Combined injection check
"safe-filename.pdf".AgainstUnsafeFileName("filename");
Console.WriteLine("   ✅ Safe filename validated!");

// Demonstrate catching attacks
try { "SELECT * FROM Users".AgainstSqlInjection("search"); }
catch (ArgumentException ex) { Console.WriteLine($"   🛡️ SQL Injection blocked: {ex.Message}"); }

try { "<script>alert('xss')</script>".AgainstXss("comment"); }
catch (ArgumentException ex) { Console.WriteLine($"   🛡️ XSS blocked: {ex.Message}"); }

try { "../../etc/passwd".AgainstPathTraversal("path"); }
catch (ArgumentException ex) { Console.WriteLine($"   🛡️ Path Traversal blocked: {ex.Message}"); }

#endregion

#region Format Guards

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 7. FORMAT GUARDS - Universal Formats (NEW v5.0)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 7. Format Guards - Universal Formats (NEW v5.0)");

try
{
    41.0082.AgainstInvalidLatitude("latitude");
    28.9784.AgainstInvalidLongitude("longitude");
    Console.WriteLine("   ✅ Geo coordinates (41.0082, 28.9784) validated!");

    "00:1A:2B:3C:4D:5E".AgainstInvalidMacAddress("mac");
    Console.WriteLine("   ✅ MAC address validated!");

    "api.example.com".AgainstInvalidHostname("hostname");
    Console.WriteLine("   ✅ Hostname validated!");

    "192.168.1.0/24".AgainstInvalidCidr("cidr");
    Console.WriteLine("   ✅ CIDR notation validated!");

    "US".AgainstInvalidCountryCode("country");
    Console.WriteLine("   ✅ ISO country code validated!");

    "Europe/Istanbul".AgainstInvalidTimeZoneId("timezone");
    Console.WriteLine("   ✅ Time zone ID validated!");

    "en-US".AgainstInvalidLanguageTag("lang");
    Console.WriteLine("   ✅ Language tag validated!");

    "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abc123".AgainstInvalidJwtFormat("token");
    Console.WriteLine("   ✅ JWT format validated!");

    "Server=localhost;Database=mydb;User=sa;Password=secret".AgainstInvalidConnectionString("connStr");
    Console.WriteLine("   ✅ Connection string validated!");
}
catch (ArgumentException ex)
{
    Console.WriteLine($"   ❌ Format validation error: {ex.Message}");
}

#endregion

#region 🌟 FastGuard (Zero-Allocation)

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 8. FASTGUARD - Span-based validation (NEW v5.0)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 8. FastGuard - Zero-Allocation Validation (NEW v5.0)");

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

Console.WriteLine("   ✅ All FastGuard span-based validations passed!");
Console.WriteLine("   📊 Zero heap allocations for hot-path validations!");

#endregion

#region 🌟 Advanced Validators

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 9. ADVANCED VALIDATORS
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 9. Advanced Validators");

try
{
    "4111111111111111".AgainstInvalidCreditCard("creditCard");
    Console.WriteLine("   ✅ Credit card validated!");

    "DE89370400440532013000".AgainstInvalidIban("iban");
    Console.WriteLine("   ✅ IBAN validated!");

    "{\"name\": \"test\"}".AgainstInvalidJson("jsonData");
    Console.WriteLine("   ✅ JSON validated!");

    "10000000146".AgainstInvalidTurkishId("tcKimlik");
    Console.WriteLine("   ✅ Turkish ID validated!");

    "SGVsbG8gV29ybGQ=".AgainstInvalidBase64("base64Data");
    Console.WriteLine("   ✅ Base64 validated!");

    "#FF5733".AgainstInvalidHexColor("color");
    Console.WriteLine("   ✅ Hex color validated!");
}
catch (ArgumentException ex)
{
    Console.WriteLine($"   ❌ Validation error: {ex.Message}");
}

#endregion

#region 🌟 Localization (9 Languages)

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 10. LOCALIZATION - 9 Languages (Enhanced v5.0)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 10. Localization Support (9 Languages)");

string[] languages = ["en", "tr", "de", "fr", "es", "pt", "ar", "ja", "it"];
foreach (var lang in languages)
{
    var msg = ValidationMessages.Get("NotNull", new CultureInfo(lang), "Email");
    Console.WriteLine($"   {lang.ToUpperInvariant()}: {msg}");
}

// Scoped culture (thread-safe)
Console.WriteLine("\n   🔒 Thread-safe scoped culture:");
ValidationMessages.SetCultureForCurrentScope(new CultureInfo("tr"));
Console.WriteLine($"   Scoped (TR): {ValidationMessages.Get("NotNull", "Field")}");

#endregion

#region 🌟 Common Profiles

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 11. BUILT-IN PROFILES
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 11. Built-in Profiles");

var emailResult2 = CommonProfiles.Email("valid@email.com");
var passwordResult2 = CommonProfiles.Password("StrongP@ss1", minLength: 8);
var usernameResult2 = CommonProfiles.Username("john_doe");

Console.WriteLine($"   ✅ Email: {(emailResult2.IsValid ? "Valid" : "Invalid")}");
Console.WriteLine($"   ✅ Password: {(passwordResult2.IsValid ? "Valid" : "Invalid")}");
Console.WriteLine($"   ✅ Username: {(usernameResult2.IsValid ? "Valid" : "Invalid")}");

#endregion

Console.WriteLine("\n" + "=".PadRight(60, '='));

#region 🎯 Legacy API (Backward Compatible)

Console.WriteLine("\n📌 Legacy API - Fully Backward Compatible!");

Guard.AgainstNull("test", "testParam");
Guard.AgainstNullOrEmpty("some text", "textParam");
Guard.AgainstOutOfRange(25, 18, 60, "age");

Guard.For("valid@email.com", "email")
     .NotNull()
     .NotEmpty()
     .Matches(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

Console.WriteLine("   ✅ Legacy guards still work!");

#endregion

#region 🎯 Real-world Service Example

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

Console.WriteLine("\n" + "=".PadRight(60, '='));
Console.WriteLine("🆕 v6.1 — DDD DOMAIN PRIMITIVES");
Console.WriteLine("=".PadRight(60, '='));

#region 🆕 v6.1: Strongly-Typed IDs (source generator)

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 12. STRONGLY-TYPED IDS — source generator (struct) + manual record
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 12. Strongly-Typed IDs (NEW v6.1)");

// --- Source-generated struct style (zero-allocation) ---
// Declared with [StronglyTypedId<TValue>] on a readonly partial struct (Domain/Ids.cs).
// The generator emits IEquatable, operators, New(), Empty, Value, plus
// EF Core ValueConverter + System.Text.Json converter + ASP.NET Core TypeConverter
// as separate .g.cs files.
ProductId p1 = ProductId.New();
ProductId p1copy = new(p1.Value);
ProductId p2 = ProductId.New();

Console.WriteLine($"   ✅ ProductId.New()         = {p1}");
Console.WriteLine($"   ✅ Value equality:          p1 == p1copy ? {p1 == p1copy}");
Console.WriteLine($"   ✅ Different ids unequal:   p1 != p2 ? {p1 != p2}");

// Non-Guid backed source-gen IDs
SkuId sku = new(42);
CountryCode tr = new("TR");
Console.WriteLine($"   ✅ SkuId (int-backed)       = {sku}");
Console.WriteLine($"   ✅ CountryCode (string)     = {tr}");

// The generated TypeConverter lets ASP.NET Core bind these from route/query:
var skuConverter = new SkuIdTypeConverter();
var skuFromString = (SkuId)skuConverter.ConvertFrom("123")!;
var skuToString = skuConverter.ConvertTo(sku, typeof(string));
Console.WriteLine($"   ✅ Generated TypeConverter: \"123\" → {skuFromString}, {sku} → \"{skuToString}\"");

// --- Manual record style ---
// Inherits StronglyTypedId<TValue> abstract record. Reference type.
// Participates in the AgainstDefaultStronglyTypedId guard (next section).
OrderId orderId = OrderId.New();
CustomerId customerId = CustomerId.New();
InvoiceId invoiceId = InvoiceId.New();

Console.WriteLine($"   ✅ OrderId (manual record)   = {orderId.Value}");
Console.WriteLine($"   ✅ CustomerId and OrderId are distinct record types even though both wrap Guid.");

#endregion

#region 🆕 v6.1: Guard.Against.DefaultStronglyTypedId

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 13. AgainstDefaultStronglyTypedId GUARD
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 13. AgainstDefaultStronglyTypedId Guard (NEW v6.1)");

// Happy path — a non-default ID passes through and returns itself for chaining.
var validated = invoiceId.AgainstDefaultStronglyTypedId(nameof(invoiceId));
Console.WriteLine($"   ✅ Valid id passed guard and returned: {validated.Value}");

// Null check — constrained generic receiver, no reflection, fully type-safe.
try
{
    InvoiceId? nullId = null;
    nullId!.AgainstDefaultStronglyTypedId(nameof(nullId));
}
catch (NullValueException)
{
    Console.WriteLine("   🛡️ Null id blocked (NullValueException).");
}

// Default (Guid.Empty) — caught by EqualityComparer<Guid>.Default.Equals(value, default).
try
{
    var emptyId = new InvoiceId(Guid.Empty);
    emptyId.AgainstDefaultStronglyTypedId(nameof(emptyId));
}
catch (ZeroValueException)
{
    Console.WriteLine("   🛡️ Guid.Empty id blocked (ZeroValueException).");
}

#endregion

#region 🆕 v6.1: Value Objects (hybrid style)

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 14. VALUE OBJECTS — abstract base class + record marker
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 14. Value Objects (NEW v6.1 — hybrid style)");

// Behavior-rich VO via abstract ValueObject base class (Money class in Domain/ValueObjects.cs).
var price1 = new Money(100m, "USD");
var price2 = new Money(100m, "USD");
var price3 = new Money(100m, "EUR");

Console.WriteLine($"   ✅ Money class-based equality: 100 USD == 100 USD ? {price1 == price2}");
Console.WriteLine($"   ✅ Currency matters:          100 USD != 100 EUR ? {price1 != price3}");
Console.WriteLine($"   ✅ Hash codes match for equals: {price1.GetHashCode() == price2.GetHashCode()}");

var doubled = price1.Add(price2);
Console.WriteLine($"   ✅ Money.Add behaviour: 100 USD + 100 USD = {doubled}");

// Pure-data VO via IValueObject marker on a record (Address record in Domain/ValueObjects.cs).
var home = new Address("Bagdat Cd. 100", "Istanbul", "34728", "TR");
var sameHome = new Address("Bagdat Cd. 100", "Istanbul", "34728", "TR");
Console.WriteLine($"   ✅ Record-based VO structural equality: {home == sameHome}");

// Constructor invariant enforcement (via Ensure.That() on each field)
try
{
    _ = new Address("", "", "", "");
}
catch (GuardException ex)
{
    Console.WriteLine($"   🛡️ Address invariant guarded ({ex.GetType().Name}): {ex.Message}");
}

#endregion

#region 🆕 v6.1: Entity<TId>

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 15. ENTITY<TId> — identity equality
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 15. Entity<TId> (NEW v6.1 — identity equality)");

var customer = new Customer(customerId, "alice@example.com", home);
var sameCustomer = new Customer(customerId, "alice+newsletter@example.com", home); // same Id, different state

Console.WriteLine($"   ✅ Customer equality by Id only: {customer == sameCustomer}");
Console.WriteLine($"   ✅ Email differs, but identity is the same → still equal.");

customer.ChangeEmail("alice.updated@example.com");
Console.WriteLine($"   ✅ Customer email updated to: {customer.Email}");

#endregion

#region 🆕 v6.1: AggregateRoot + Domain Events + Business Rules

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 16. AGGREGATE ROOT + DOMAIN EVENTS + BUSINESS RULES
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 16. AggregateRoot + Domain Events + Business Rules (NEW v6.1)");

var order = new Order(orderId, customerId);
order.AddItem(new Money(49.99m, "USD"));
order.AddItem(new Money(19.99m, "USD"));

Console.WriteLine($"   ✅ Order created with total = {order.Total}");

// Business rule: OrderMustHaveItemsRule (sync) — enforced by Order.Place() via CheckRule helper.
order.Place();
Console.WriteLine($"   ✅ Order.Place() succeeded. Status = {order.Status}");

// Business rule failure path: try to ship an unpaid order.
var unpaidOrder = new Order(OrderId.New(), customerId);
unpaidOrder.AddItem(new Money(9.99m, "USD"));
// unpaidOrder is still Pending — it hasn't been Place()d yet.
try
{
    unpaidOrder.Ship();
}
catch (BusinessRuleValidationException ex)
{
    Console.WriteLine($"   🛡️ Ship blocked by rule {ex.RuleName}: {ex.Message}");
}

// Ship the paid order — raises OrderShippedEvent.
order.Ship();
Console.WriteLine($"   ✅ Order.Ship() succeeded. Status = {order.Status}");

// PullDomainEvents returns the buffered events AND clears them (double-dispatch prevention).
var events = order.PullDomainEvents();
Console.WriteLine($"   📤 Pulled {events.Count} domain event(s) from the aggregate:");
foreach (var domainEvent in events)
{
    Console.WriteLine($"      · {domainEvent.GetType().Name} @ {domainEvent.OccurredOnUtc:HH:mm:ss} (EventId={domainEvent.EventId})");
}
Console.WriteLine($"   ✅ After Pull, aggregate's DomainEvents is empty: {order.DomainEvents.Count == 0}");

// Async business rule — demonstrates CheckRuleAsync path.
var uniqueRule = new CustomerEmailMustBeUniqueRule(
    "alice@example.com",
    existsInStore: async email =>
    {
        await Task.Delay(5); // simulated I/O
        return email == "alice@example.com"; // pretend this address is already taken
    });

try
{
    // Simulate the aggregate running the async rule.
    if (await uniqueRule.IsBrokenAsync())
        throw new BusinessRuleValidationException(uniqueRule);
}
catch (BusinessRuleValidationException ex)
{
    Console.WriteLine($"   🛡️ Async rule broken: {ex.RuleName} — {ex.Message}");
}

#endregion

#region 🆕 v6.1: DI — AddOrionGuardStronglyTypedIds

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 17. AddOrionGuardStronglyTypedIds — DI registration
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 17. AddOrionGuardStronglyTypedIds (NEW v6.1)");

var services = new ServiceCollection();
services.AddOrionGuardStronglyTypedIds(typeof(Program).Assembly);

var provider = services.BuildServiceProvider();
var registrationCount = services.Count(d => d.ServiceType.Name.EndsWith("EfCoreValueConverter", StringComparison.Ordinal));
Console.WriteLine($"   ✅ Registered {registrationCount} generated EF Core ValueConverter(s) as singletons.");
Console.WriteLine("   ℹ The generator skips emitting EF Core ValueConverter companions when the consumer project does not reference Microsoft.EntityFrameworkCore (NEW in v6.2).");
Console.WriteLine("   ℹ Add `<PackageReference Include=\"Microsoft.EntityFrameworkCore\" />` to resume emitting them. JSON + TypeConverter companions emit unconditionally.");

#endregion

#region 🆕 v6.1: New Localization Keys

// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
// 18. NEW LOCALIZATION KEYS (14 languages each)
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Console.WriteLine("\n📌 18. New DDD Localization Keys (NEW v6.1 — 14 languages)");

// Reset culture so we pick per-language messages deliberately.
ValidationMessages.SetCultureForCurrentScope(CultureInfo.InvariantCulture);

string[] sampleLangs = ["en", "tr", "de", "ja", "ar"];
foreach (var lang in sampleLangs)
{
    var ci = new CultureInfo(lang);
    var msg = ValidationMessages.Get("DefaultStronglyTypedId", ci, "OrderId");
    Console.WriteLine($"   {lang.ToUpperInvariant()}: {msg}");
}

Console.WriteLine("   ℹ Two more keys also added to all 14 languages: BusinessRuleBroken, DomainInvariantViolated.");

#endregion

Console.WriteLine("\n" + "=".PadRight(60, '='));
Console.WriteLine("🎉 All OrionGuard v6.1 demos completed successfully!");
Console.WriteLine("   (v5 core + v6.0 ecosystem + v6.1 DDD primitives)");
Console.WriteLine("=".PadRight(60, '='));

await Moongazing.OrionGuard.Demo.DomainEventsDemo.RunAsync();

