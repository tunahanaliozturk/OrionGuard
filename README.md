<p align="center">
  <img src="src/Moongazing.OrionGuard/docs/logo.png" alt="OrionGuard Logo" width="150" />
</p>

<h1 align="center">OrionGuard</h1>

<p align="center">
  A modern, fluent, and extensible guard clause &amp; validation library for .NET
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/OrionGuard"><img src="https://img.shields.io/nuget/v/OrionGuard?style=flat-square&color=blue" alt="NuGet" /></a>
  <a href="https://www.nuget.org/packages/OrionGuard"><img src="https://img.shields.io/nuget/dt/OrionGuard?style=flat-square&color=green" alt="Downloads" /></a>
  <a href="https://github.com/Moongazing/OrionGuard/blob/master/src/Moongazing.OrionGuard/docs/LICENSE.txt"><img src="https://img.shields.io/badge/license-MIT-yellow?style=flat-square" alt="License" /></a>
  <img src="https://img.shields.io/badge/.NET-8.0%20%7C%209.0-purple?style=flat-square" alt="Target" />
</p>

---

> **v5.0.1 is out!** Security Guards, Format Guards, ThrowHelper, span-based FastGuard, and 8-language localization.
> [See what's new in this release.](https://github.com/tunahanaliozturk/OrionGuard/releases/tag/v5.0.1)

---

## Features

- **Fluent API** — `Ensure.That(email).NotNull().Email()` with automatic parameter name capture
- **Security Guards** — SQL injection, XSS, path traversal, command injection, LDAP, XXE detection
- **Format Guards** — Coordinates, MAC, CIDR, hostname, JWT, country codes, time zones, Base64
- **FastGuard** — Span-based zero-allocation validation with aggressive inlining
- **ThrowHelper** — JIT-optimized exception throwing for near-zero happy-path overhead
- **Result Pattern** — Collect all validation errors instead of throwing on the first failure
- **Async Validation** — Database lookups, API calls, and external service checks
- **Object Validation** — Property expressions, cross-property rules, compiled expression caching
- **Business Guards** — Money, currency, SKU, coupon, scheduling, status transitions
- **Conditional Validation** — `When` / `Unless` / `Always` control flow
- **Attribute-Based** — `[NotNull]`, `[Email]`, `[Range]`, `[Length]` on model properties
- **Dependency Injection** — `services.AddOrionGuard()` with `AbstractValidator<T>` support
- **Localization** — Thread-safe, 8 languages (EN, TR, DE, FR, ES, PT, AR, JA), per-request culture
- **Common Profiles** — Pre-built validators for email, password, username, phone, money, etc.

---

## Installation

```bash
dotnet add package OrionGuard
```

---

## Quick Start

```csharp
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Extensions;

// Fluent guard clauses
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(age).InRange(18, 120);

// High-performance path
FastGuard.NotNullOrEmpty(name, nameof(name));
FastGuard.Email(email, nameof(email));

// Security checks
userInput.AgainstSqlInjection(nameof(userInput));
userInput.AgainstXss(nameof(userInput));
filePath.AgainstPathTraversal(nameof(filePath));

// Or all security checks at once
userInput.AgainstInjection(nameof(userInput));

// Format validation
"US".AgainstInvalidCountryCode(nameof(country));
"Europe/Istanbul".AgainstInvalidTimeZoneId(nameof(tz));
token.AgainstInvalidJwtFormat(nameof(token));

// Collect all errors
var result = GuardResult.Combine(
    Ensure.Accumulate(email, "Email").NotNull().Email().ToResult(),
    Ensure.Accumulate(password, "Password").MinLength(8).ToResult()
);
if (result.IsInvalid)
    return BadRequest(result.ToErrorDictionary());
```

---

## Fluent API

```csharp
// Automatic parameter name capture via CallerArgumentExpression
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(password).NotNull().MinLength(8).Matches(@"^(?=.*[A-Z])");

// Shorthand — returns validated value
string validEmail = Ensure.NotNull(email);
int validAge = Ensure.InRange(age, 18, 120);

// Transform and default
var cleaned = Ensure.That(rawEmail)
    .Transform(e => e.Trim().ToLowerInvariant())
    .Default("unknown@example.com")
    .NotEmpty()
    .Email()
    .Value;
```

**Available validations:** `NotNull`, `NotEmpty`, `Email`, `Url`, `Matches`, `Length`, `MinLength`, `MaxLength`, `GreaterThan`, `LessThan`, `InRange`, `Positive`, `NotNegative`, `InPast`, `InFuture`, `MinCount`, `MaxCount`, `NoNullItems`, `Must`

---

## Security Guards

Built-in protection against common attack vectors. All patterns stored in `FrozenSet<string>` for O(1) lookups.

```csharp
// Individual checks
userInput.AgainstSqlInjection(nameof(userInput));     // 28 SQL patterns
userInput.AgainstXss(nameof(userInput));               // 28 XSS vectors
filePath.AgainstPathTraversal(nameof(filePath));       // Traversal sequences + encoded variants
command.AgainstCommandInjection(nameof(command));      // Shell metacharacters + interpreters
ldapFilter.AgainstLdapInjection(nameof(ldapFilter));   // LDAP-special characters
xmlInput.AgainstXxe(nameof(xmlInput));                 // DOCTYPE/ENTITY declarations
fileName.AgainstUnsafeFileName(nameof(fileName));      // Traversal + invalid OS characters
redirectUrl.AgainstOpenRedirect(nameof(redirectUrl), trustedDomains);

// Combined — runs SQL, XSS, path traversal, and command injection
userInput.AgainstInjection(nameof(userInput));
```

---

## Format Guards

Universal format validation for internationally applicable data.

```csharp
// Geographic
latitude.AgainstInvalidLatitude(nameof(latitude));           // -90 to 90
longitude.AgainstInvalidLongitude(nameof(longitude));         // -180 to 180
FormatGuards.AgainstInvalidCoordinates(lat, lng, "location");

// Network
"AA:BB:CC:DD:EE:FF".AgainstInvalidMacAddress(nameof(mac));   // Colon, hyphen, or dot notation
"api.example.com".AgainstInvalidHostname(nameof(host));       // RFC 1123
"192.168.1.0/24".AgainstInvalidCidr(nameof(cidr));            // IPv4 and IPv6

// International standards
"US".AgainstInvalidCountryCode(nameof(country));              // ISO 3166-1 alpha-2 (249 codes)
"Europe/Istanbul".AgainstInvalidTimeZoneId(nameof(tz));       // IANA/Windows time zones
"en-US".AgainstInvalidLanguageTag(nameof(lang));              // BCP 47

// Tokens and encoding
token.AgainstInvalidJwtFormat(nameof(token));                 // Three Base64URL segments
connStr.AgainstInvalidConnectionString(nameof(connStr));      // Key=value format
encoded.AgainstInvalidBase64String(nameof(encoded));          // Structure + padding
```

---

## FastGuard (High Performance)

Aggressively inlined guards with `ThrowHelper` pattern for maximum JIT optimization. Span-based methods for zero-allocation validation.

```csharp
// Standard guards
FastGuard.NotNull(user, nameof(user));
FastGuard.NotNullOrEmpty(email, nameof(email));
FastGuard.InRange(age, 0, 150, nameof(age));
FastGuard.Positive(quantity, nameof(quantity));

// Span-based — zero allocation
FastGuard.Email(email, nameof(email));                    // No regex, pure span parsing
FastGuard.Ascii(input.AsSpan(), nameof(input));
FastGuard.AlphaNumeric(code.AsSpan(), nameof(code));
FastGuard.NumericString(digits.AsSpan(), nameof(digits));
FastGuard.MaxLength(name.AsSpan(), 100, nameof(name));
FastGuard.ValidGuid(id.AsSpan(), nameof(id));
FastGuard.Finite(price, nameof(price));                   // Rejects NaN/Infinity
```

---

## Result Pattern

Collect all errors for API-friendly responses.

```csharp
var result = GuardResult.Combine(
    Ensure.Accumulate(email, "Email").NotNull().Email().ToResult(),
    Ensure.Accumulate(password, "Password").MinLength(8).ToResult(),
    Ensure.Accumulate(age, "Age").InRange(18, 120).ToResult()
);

if (result.IsInvalid)
{
    Dictionary<string, string[]> errors = result.ToErrorDictionary();
    // { "Email": ["Email must be a valid email address."], ... }
}

// Or throw all at once
result.ThrowIfInvalid();
```

---

## Async Validation

```csharp
var result = await EnsureAsync.That(email, "Email")
    .UniqueAsync(async e => await db.IsEmailUniqueAsync(e))
    .ExistsAsync(async e => await emailService.IsDeliverableAsync(e))
    .MustAsync(async e => await IsNotBlacklistedAsync(e), "Email is blacklisted")
    .ValidateAsync();
```

---

## Object Validation

```csharp
var result = Validate.For(userDto)
    .Property(u => u.Email, g => g.NotNull().Email())
    .Property(u => u.Password, g => g.NotNull().MinLength(8))
    .Property(u => u.Age, g => g.InRange(18, 120))
    .CrossProperty<DateTime, DateTime>(
        o => o.StartDate, o => o.EndDate,
        (start, end) => start < end,
        "StartDate must be before EndDate")
    .When(order.IsExpress, v =>
        v.Property(o => o.Priority, g => g.InRange(1, 3)))
    .ToResult();
```

---

## Conditional Validation

```csharp
Ensure.That(alternateEmail)
    .When(isPrimaryEmailInvalid)
    .NotNull()
    .Email()
    .Unless(isGuestUser)
    .MinLength(5)
    .Always()
    .NotEmpty();
```

---

## Business Domain Guards

```csharp
// Financial
price.AgainstInvalidMonetaryAmount("amount", maxDecimalPlaces: 2);
"USD".AgainstInvalidCurrencyCode("currency");
discount.AgainstInvalidPercentage("discount");

// E-commerce
"PROD-12345-XL".AgainstInvalidSku("sku");
total.AgainstOrderBelowMinimum(50m, "orderTotal");
"SUMMER2025".AgainstInvalidCouponCode("coupon");

// Scheduling
orderDate.AgainstWeekend("deliveryDate");
appointmentTime.AgainstOutsideBusinessHours("time", startHour: 9, endHour: 17);

// Advanced formats
"4111111111111111".AgainstInvalidCreditCard("card");       // Luhn algorithm
"DE89370400440532013000".AgainstInvalidIban("iban");
"1.2.3-beta.1".AgainstInvalidSemVer("version");
```

---

## Attribute-Based Validation

```csharp
public class CreateUserRequest
{
    [NotNull, Email]
    public string Email { get; set; }

    [NotEmpty, Length(8, 128)]
    public string Password { get; set; }

    [Range(18, 120), Positive]
    public int Age { get; set; }
}

var result = AttributeValidator.Validate(request);
if (result.IsInvalid)
    return BadRequest(result.ToErrorDictionary());
```

---

## Dependency Injection

```csharp
// Registration
services.AddOrionGuard();
services.AddValidator<CreateUserRequest, CreateUserRequestValidator>();

// Validator
public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator(IUserRepository userRepo)
    {
        RuleFor(x => x.Email, "Email", v => v.NotNull().Email());
        RuleForAsync(
            async x => await userRepo.IsEmailUniqueAsync(x.Email),
            "Email already registered", "Email");
    }
}

// Controller
[HttpPost]
public async Task<IActionResult> Create(CreateUserRequest request)
{
    var result = await _validator.ValidateAsync(request);
    if (result.IsInvalid) return BadRequest(result.ToErrorDictionary());
    return Ok();
}
```

---

## Localization

Thread-safe, per-request culture scoping with `AsyncLocal<CultureInfo>`. 8 built-in languages with English fallback.

```csharp
// Per-request scope (thread-safe)
ValidationMessages.SetCultureForCurrentScope(new CultureInfo("de"));

// Global
ValidationMessages.SetCulture("tr");

// Custom translations
ValidationMessages.AddMessages("es", new Dictionary<string, string>
{
    ["NotNull"] = "{0} no puede ser nulo.",
    ["Email"]   = "{0} debe ser una direccion de correo valida."
});
```

**Supported:** English, Turkish, German, French, Spanish, Portuguese, Arabic, Japanese

---

## Code Contracts

```csharp
public decimal CalculateDiscount(decimal price, decimal percent)
{
    Contract.Requires(price >= 0, "Price must be non-negative");
    Contract.Requires(percent is >= 0 and <= 100, "Invalid percentage");

    var result = price * (1 - percent / 100);

    Contract.Ensures(result >= 0, "Result cannot be negative");
    return result;
}
```

---

## Common Profiles

```csharp
CommonProfiles.Email("user@example.com");
CommonProfiles.Password("SecureP@ss1", requireUppercase: true, requireSpecialChar: true);
CommonProfiles.Username("john_doe", minLength: 3, maxLength: 30);
CommonProfiles.BirthDate(birthDate, minAge: 18);
CommonProfiles.MonetaryAmount(99.99m, min: 0, max: 10000);
CommonProfiles.GuidId(userId);
CommonProfiles.Slug("my-article-title");
```

---

## Performance

- **ThrowHelper pattern** — Smaller method bodies for better JIT inlining
- **FrozenSet** — O(1) pattern matching for security and business guards
- **Compiled expression caching** — No repeated reflection in ObjectValidator
- **RegexCache** — Bounded at 1,000 entries, no recompilation
- **ReadOnlySpan** — Zero-allocation FastGuard methods
- **Explicit StringComparison** — No culture-sensitive overhead

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full history of changes.

## Contributing

Contributions are welcome! Please open an issue or submit a pull request.

## License

[MIT](src/Moongazing.OrionGuard/docs/LICENSE.txt)

## Author

**Tunahan Ali Ozturk** — [GitHub](https://github.com/Moongazing) · [LinkedIn](https://www.linkedin.com/in/tunahanaliozturk/) · [NuGet](https://www.nuget.org/packages/OrionGuard)
