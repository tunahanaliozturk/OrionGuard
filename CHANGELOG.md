# Changelog

All notable changes to OrionGuard will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [5.0.0] - 2026-04-02

### Breaking Changes

- All exception classes are now `sealed` and include `ErrorCode` and `ParameterName` properties.
- `Validate.Object<T>()` renamed to `Validate.For<T>()`.
- `Validate.ObjectStrict<T>()` renamed to `Validate.ForStrict<T>()`.
- `FastGuard.Guid()` renamed to `FastGuard.ValidGuid()`.
- Removed `TurkishGuards` -- replaced by the universal `FormatGuards` class.
- `AgainstEmptyCollection` now throws `NullValueException` instead of `EmptyStringException` (bug fix).
- `AgainstNotAllLowercase` behavior corrected -- previously always passed due to a self-comparison bug.

### Added

#### ThrowHelper Pattern
- New `ThrowHelper` static class with `[DoesNotReturn]` and `[StackTraceHidden]` attributes.
- All hot-path guard methods delegate throwing to ThrowHelper for smaller JIT-compiled method bodies.

#### Span-Based FastGuard Methods
- `FastGuard.Email(string, string)` -- validates email format using zero-allocation span parsing.
- `FastGuard.Ascii(ReadOnlySpan<char>, string)` -- ensures all characters are within the ASCII range.
- `FastGuard.AlphaNumeric(ReadOnlySpan<char>, string)` -- rejects non-alphanumeric characters.
- `FastGuard.NumericString(ReadOnlySpan<char>, string)` -- digits-only span validation.
- `FastGuard.MaxLength(ReadOnlySpan<char>, int, string)` -- maximum length check on spans.
- `FastGuard.ValidGuid(ReadOnlySpan<char>, string)` -- GUID format and non-empty check.
- `FastGuard.Finite(double, string)` -- rejects NaN and Infinity.

#### Security Guards (new file)
- `AgainstSqlInjection` -- detects 28 common SQL injection patterns using a `FrozenSet`.
- `AgainstXss` -- detects 28 cross-site scripting vectors including event handlers and DOM sinks.
- `AgainstPathTraversal` -- catches directory traversal sequences and common encoded variants.
- `AgainstCommandInjection` -- blocks shell metacharacters, pipe operators, and known interpreters.
- `AgainstLdapInjection` -- span-based detection of LDAP-special characters.
- `AgainstXxe` -- detects DOCTYPE and ENTITY declarations indicative of XXE attacks.
- `AgainstInjection` -- combined check that runs SQL, XSS, path traversal, and command injection in one call.
- `AgainstUnsafeFileName` -- validates filenames against path traversal and invalid OS characters.
- `AgainstOpenRedirect` -- validates redirect URLs against an allow-list of trusted domains.

#### Format Guards (new file, replaces TurkishGuards)
- `AgainstInvalidLatitude` / `AgainstInvalidLongitude` / `AgainstInvalidCoordinates` -- geographic coordinate validation.
- `AgainstInvalidMacAddress` -- validates MAC addresses in colon, hyphen, or dot-separated notation.
- `AgainstInvalidHostname` -- RFC 1123 hostname validation including label length and character rules.
- `AgainstInvalidCidr` -- validates CIDR notation for both IPv4 and IPv6 addresses.
- `AgainstInvalidCountryCode` -- checks against the full ISO 3166-1 alpha-2 set (249 codes).
- `AgainstInvalidTimeZoneId` -- validates against the system's IANA/Windows time zone database.
- `AgainstInvalidLanguageTag` -- BCP 47 / IETF language tag validation using CultureInfo.
- `AgainstInvalidJwtFormat` -- structural validation of JWT tokens (three Base64URL segments).
- `AgainstInvalidConnectionString` -- checks for well-formed key=value connection string pairs.
- `AgainstInvalidBase64String` -- validates Base64 encoding structure and padding.

#### ObjectValidator Enhancements
- Compiled expression caching via `ConcurrentDictionary` to avoid repeated `Expression.Compile()` overhead.
- `CrossProperty<TProp1, TProp2>()` for cross-property validation with a custom predicate.
- `When(bool, Action<ObjectValidator<T>>)` for conditional validation blocks.

#### FluentGuard Enhancements
- `Transform(Func<T, T>)` -- in-pipeline value transformation (trim, lowercase, etc.).
- `Default(T)` -- replaces null values with a specified default during validation.
- All date comparisons now use `DateTime.UtcNow`.

#### Thread-Safe Localization
- `ValidationMessages` rewritten with `ConcurrentDictionary` and `AsyncLocal<CultureInfo>`.
- `SetCultureForCurrentScope(CultureInfo)` for per-request culture without affecting other threads.
- Expanded to 8 languages: English, Turkish, German, French, Spanish, Portuguese, Arabic, Japanese.
- 30 message keys per major language with fallback to English for missing entries.

#### Infrastructure
- `GuardProfileRegistry` now uses `ConcurrentDictionary` for thread-safe profile registration.
- Added `TryExecute<T>()`, `IsRegistered()`, and `Remove()` methods to `GuardProfileRegistry`.
- `RegexCache` with bounded size (`MaxCacheSize = 1000`) replaces all raw `Regex.IsMatch` calls.
- CI/CD workflow added: builds and tests on every push, publishes to NuGet.org and GitHub Packages on release.

### Changed

- All `DateTime.Now` references replaced with `DateTime.UtcNow` across DateTimeGuards and FluentGuard.
- All `Regex.IsMatch()` calls replaced with `RegexCache.IsMatch()` (StringGuards, AdvancedStringGuards, BusinessGuards).
- `BusinessGuards` currency codes now stored in a static `FrozenSet<string>` instead of allocating a new `HashSet` per call.
- `CollectionGuards.AgainstExceedingCount` optimized: checks `ICollection<T>.Count` or `IReadOnlyCollection<T>.Count` first, falls back to `Take(n+1).Count()` for unenumerated sequences.
- `FileGuards.AgainstInvalidFileExtension` now uses `StringComparer.OrdinalIgnoreCase` for extension matching.
- All `StartsWith` / `EndsWith` calls include an explicit `StringComparison` parameter.
- `AnalysisLevel` set to `latest-recommended` with `TreatWarningsAsErrors` enabled.
- `GeneratePackageOnBuild` set to `false` (packing handled by CI pipeline).

### Fixed

- `AgainstNotAllLowercase` was comparing the string to itself with `InvariantCultureIgnoreCase`, which always returned true. Now compares against `ToLowerInvariant()` with `Ordinal` comparison.
- `AgainstEmptyCollection` was throwing `EmptyStringException` for empty collections. Now correctly throws `NullValueException`.
- `GuardBuilderExtensions` was passing `.Value` instead of `.ParameterName` in error messages.
- Missing `using System.Diagnostics` in ThrowHelper and FastGuard caused `StackTraceHidden` build errors.
- Multiple CA analyzer violations resolved: CA1305, CA1310, CA1720, CA1845, CA1862, CA2263.

### Removed

- `TurkishGuards.cs` -- country-specific validations removed in favor of universal `FormatGuards`.

---

## [4.0.0] - 2025-01-XX

### ?? Major Features

#### New Fluent API with `Ensure.That()`
```csharp
// Automatic parameter name capture with CallerArgumentExpression
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(password).NotNull().MinLength(8).Matches(@"^(?=.*[A-Z])");

// Shorthand methods
string validEmail = Ensure.NotNull(email);
string validName = Ensure.NotNullOrEmpty(name);
int validAge = Ensure.InRange(age, 18, 120);
```

#### Result Pattern with Error Accumulation
```csharp
// Collect all errors instead of throwing on first
var result = GuardResult.Combine(
    Ensure.Accumulate(email, "Email").NotNull().Email().ToResult(),
    Ensure.Accumulate(password, "Password").MinLength(8).ToResult(),
    Ensure.Accumulate(username, "Username").Length(3, 30).ToResult()
);

if (result.IsInvalid)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"[{error.ParameterName}]: {error.Message}");
    }
}

// API-friendly error format
Dictionary<string, string[]> errors = result.ToErrorDictionary();
```

#### Async Validation Support
```csharp
var result = await EnsureAsync.That(email, "Email")
    .UniqueAsync(async e => await userRepository.IsEmailUniqueAsync(e))
    .ExistsAsync(async e => await emailService.IsDeliverableAsync(e))
    .ValidateAsync();
```

#### Conditional Validation (When/Unless)
```csharp
Ensure.That(secondaryEmail)
    .When(isPrimaryEmailInvalid)     // Only validate when condition is true
    .NotNull()
    .Email()
    .Unless(isGuestUser)             // Skip when condition is true
    .MinLength(5)
    .Always()                        // Reset - always validate from here
    .NotEmpty();
```

#### Object Validation with Property Expressions
```csharp
var result = Validate.Object(userDto)
    .Property(u => u.Email, g => g.NotNull().Email())
    .Property(u => u.Password, g => g.NotNull().MinLength(8))
    .Property(u => u.Age, g => g.InRange(18, 120))
    .NotNull(u => u.CreatedAt)
    .Must(u => u.Role, r => r != "Admin", "Cannot self-assign admin role")
    .ToResult();
```

#### Code Contracts
```csharp
public decimal CalculateDiscount(decimal price, decimal discountPercent)
{
    // Preconditions
    Contract.Requires(price >= 0, "Price must be non-negative");
    Contract.Requires(discountPercent >= 0 && discountPercent <= 100, "Invalid discount");
    
    var result = price * (1 - discountPercent / 100);
    
    // Postcondition
    Contract.Ensures(result >= 0 && result <= price, "Result must be valid");
    
    return result;
}
```

#### High-Performance Guards (FastGuard)
```csharp
// Optimized for hot paths with aggressive inlining
FastGuard.NotNullOrEmpty(email, nameof(email));
FastGuard.InRange(age, 0, 150, nameof(age));
FastGuard.Positive(quantity, nameof(quantity));

// Span-based validation for zero allocations
FastGuard.NotEmpty(dataSpan, nameof(dataSpan));
```

#### Logical Guards (AND/OR)
```csharp
// OR logic - any condition passing is sufficient
var phoneOrEmail = contact.EitherOr("Contact")
    .Or(c => IsValidEmail(c), "valid email")
    .Or(c => IsValidPhone(c), "valid phone")
    .Validate();

// AND logic with short-circuit
var result = value.AllOf("Value", shortCircuit: true)
    .And(v => v != null, "cannot be null")
    .And(v => v.Length > 0, "cannot be empty")
    .And(v => v.Length < 100, "too long")
    .ToResult();
```

### ?? New Features

#### Advanced String Validators
- **Credit Card** - Luhn algorithm validation (Visa, MasterCard)
- **IBAN** - International Bank Account Number
- **JSON/XML** - Structure validation
- **Base64** - Encoding validation
- **Turkish ID (TC Kimlik)** - National ID validation
- **Hex Color** - Color code validation
- **Semantic Version** - SemVer format
- **URL Slug** - URL-friendly format

```csharp
"4111111111111111".AgainstInvalidCreditCard("card");
"DE89370400440532013000".AgainstInvalidIban("iban");
"10000000146".AgainstInvalidTurkishId("tcNo");
"{\"key\": \"value\"}".AgainstInvalidJson("data");
"#FF5733".AgainstInvalidHexColor("color");
"1.2.3-beta.1".AgainstInvalidSemVer("version");
```

#### Business Domain Guards
```csharp
// Money & Currency
amount.AgainstInvalidMonetaryAmount("price", maxDecimalPlaces: 2);
"TRY".AgainstInvalidCurrencyCode("currency");
discount.AgainstInvalidPercentage("discount");

// E-commerce
total.AgainstOrderBelowMinimum(minimumOrder: 50m, "orderTotal");
"SUMMER2025".AgainstInvalidCouponCode("coupon");
"SKU-12345-XL".AgainstInvalidSku("productSku");

// Business Hours
orderDate.AgainstWeekend("deliveryDate");
appointmentTime.AgainstOutsideBusinessHours("appointment", startHour: 9, endHour: 18);

// Rating & Review
rating.AgainstInvalidRating("stars", minRating: 1, maxRating: 5);
reviewText.AgainstInvalidReviewText("review", minLength: 10, maxLength: 5000);
```

#### Validation Attributes
```csharp
public class CreateUserRequest
{
    [NotNull, Email]
    public string Email { get; set; }
    
    [NotEmpty, Length(8, 128)]
    public string Password { get; set; }
    
    [Range(18, 120)]
    public int Age { get; set; }
    
    [Regex(@"^\+?[1-9]\d{1,14}$")]
    public string? Phone { get; set; }
}

// Validate using attributes
var result = AttributeValidator.Validate(request);
```

#### Dependency Injection Integration
```csharp
// Register in Startup/Program.cs
services.AddOrionGuard();
services.AddValidator<CreateUserRequest, CreateUserRequestValidator>();

// Create custom validator
public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email, "Email", v => v.NotNull().NotEmpty().Email());
        RuleFor(x => x.Password, "Password", v => v.NotNull().MinLength(8));
        RuleForAsync(async x => await IsEmailUnique(x.Email), "Email already exists", "Email");
    }
}

// Use in controller
public class UsersController
{
    private readonly IValidator<CreateUserRequest> _validator;
    
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var result = await _validator.ValidateAsync(request);
        if (result.IsInvalid)
            return BadRequest(result.ToErrorDictionary());
        // ...
    }
}
```

#### Localization Support
```csharp
// Set culture globally
ValidationMessages.SetCulture("tr");

// Get localized messages
var message = ValidationMessages.Get("NotNull", "Email");
// TR: "Email bo? olamaz."
// EN: "Email cannot be null."
// DE: "Email darf nicht null sein."

// Add custom translations
ValidationMessages.AddMessages("es", new Dictionary<string, string>
{
    ["NotNull"] = "{0} no puede ser nulo.",
    ["Email"] = "{0} debe ser una direcci�n de correo v�lida."
});
```

#### Common Validation Profiles
```csharp
var emailResult = CommonProfiles.Email("user@example.com");
var passwordResult = CommonProfiles.Password("SecureP@ss1", 
    minLength: 8, 
    requireUppercase: true,
    requireSpecialChar: true);
var usernameResult = CommonProfiles.Username("john_doe", minLength: 3, maxLength: 30);
var phoneResult = CommonProfiles.PhoneNumber("+905551234567");
var birthDateResult = CommonProfiles.BirthDate(birthDate, minAge: 18, maxAge: 120);
var moneyResult = CommonProfiles.MonetaryAmount(99.99m, min: 0, max: 10000);
```

### ? Performance Improvements

- **Regex Caching**: Compiled regex patterns are cached for reuse
- **Span-based Operations**: Zero-allocation validation for hot paths
- **Aggressive Inlining**: Critical paths optimized with `MethodImplOptions.AggressiveInlining`
- **Short-circuit Evaluation**: Optional early exit on first failure
- **Debug Guards**: Conditional compilation for development-only assertions

### ?? Breaking Changes

- Minimum target framework changed to .NET 8.0 (still supports .NET 9.0)
- `Guard.For<T>()` now returns `GuardBuilder<T>` for legacy compatibility
- New `Ensure.That<T>()` is the recommended entry point for v4.0

### ?? Package Changes

- Added dependency: `Microsoft.Extensions.DependencyInjection.Abstractions`
- Multi-targeting: net8.0, net9.0

### ?? New Files Added

```
src/Moongazing.OrionGuard/
??? Core/
?   ??? Ensure.cs              # New fluent API entry point
?   ??? FluentGuard.cs         # Enhanced fluent builder
?   ??? GuardResult.cs         # Result pattern implementation
?   ??? AsyncGuard.cs          # Async validation support
?   ??? ObjectValidator.cs     # Object property validation
?   ??? Contract.cs            # Code contracts
?   ??? FastGuard.cs           # High-performance guards
?   ??? LogicalGuards.cs       # AND/OR logic
??? Attributes/
?   ??? ValidationAttributes.cs # Attribute-based validation
??? DependencyInjection/
?   ??? ServiceCollectionExtensions.cs
??? Extensions/
?   ??? AdvancedStringGuards.cs # Credit card, IBAN, etc.
?   ??? BusinessGuards.cs       # Domain-specific guards
??? Localization/
?   ??? ValidationMessages.cs   # Multi-language support
??? Profiles/
    ??? CommonProfiles.cs       # Pre-built validation profiles
```

---

## [3.0.0] - Previous Release

- Initial fluent API with `Guard.For<T>()`
- Basic validation profiles
- Extension methods for common types
- Custom exception types

---

## Migration Guide (3.x ? 4.0)

### Recommended Changes

```csharp
// Before (v3.x) - Still works!
Guard.For(email, nameof(email)).NotNull().NotEmpty();

// After (v4.0) - Recommended
Ensure.That(email).NotNull().NotEmpty();
```

### New Error Handling Pattern

```csharp
// Before (v3.x)
try {
    Guard.For(email, "email").Email();
} catch (InvalidEmailException ex) {
    // Handle
}

// After (v4.0) - Result pattern
var result = Ensure.Accumulate(email, "email").Email().ToResult();
if (result.IsInvalid) {
    return BadRequest(result.ToErrorDictionary());
}
```

---

## Authors

- **Tunahan Ali Ozturk** - *Creator & Maintainer*

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE.txt) file for details.
