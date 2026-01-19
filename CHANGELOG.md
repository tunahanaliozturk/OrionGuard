# Changelog

All notable changes to OrionGuard will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
    ["Email"] = "{0} debe ser una dirección de correo válida."
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
