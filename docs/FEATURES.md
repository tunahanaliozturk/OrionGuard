# OrionGuard v4.0 - What's New

> ?? **Complete Feature Reference for v4.0**

This document provides a comprehensive overview of all new features introduced in OrionGuard v4.0.

---

## Table of Contents

1. [Fluent API with Ensure.That()](#1-fluent-api-with-ensurethat)
2. [Result Pattern](#2-result-pattern)
3. [Async Validation](#3-async-validation)
4. [Conditional Validation](#4-conditional-validation)
5. [Object Validation](#5-object-validation)
6. [Code Contracts](#6-code-contracts)
7. [High-Performance Guards](#7-high-performance-guards)
8. [Logical Guards](#8-logical-guards)
9. [Advanced Validators](#9-advanced-validators)
10. [Business Domain Guards](#10-business-domain-guards)
11. [Attribute-Based Validation](#11-attribute-based-validation)
12. [Dependency Injection](#12-dependency-injection)
13. [Localization](#13-localization)
14. [Common Profiles](#14-common-profiles)

---

## 1. Fluent API with Ensure.That()

The new `Ensure.That()` API provides a modern, fluent way to validate values with automatic parameter name capture.

### Basic Usage

```csharp
using Moongazing.OrionGuard.Core;

// Parameter name is automatically captured
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(password).NotNull().MinLength(8);
Ensure.That(age).NotNegative().InRange(0, 150);
```

### Shorthand Methods

```csharp
// Quick null checks that return the value
string validEmail = Ensure.NotNull(userInput.Email);
string validName = Ensure.NotNullOrEmpty(userInput.Name);
int validAge = Ensure.InRange(userInput.Age, 18, 120);
```

### Available Validations

| Method | Description |
|--------|-------------|
| `NotNull()` | Value is not null |
| `NotDefault()` | Value is not default(T) |
| `NotEmpty()` | String/Collection is not empty |
| `Length(min, max)` | String length in range |
| `MinLength(min)` | Minimum string length |
| `MaxLength(max)` | Maximum string length |
| `Email()` | Valid email format |
| `Url()` | Valid URL format |
| `Matches(pattern)` | Regex pattern match |
| `StartsWith(prefix)` | String starts with |
| `EndsWith(suffix)` | String ends with |
| `Contains(substring)` | String contains |
| `GreaterThan(min)` | Numeric > min |
| `LessThan(max)` | Numeric < max |
| `InRange(min, max)` | Numeric in range |
| `Positive()` | Numeric > 0 |
| `NotNegative()` | Numeric >= 0 |
| `NotZero()` | Numeric != 0 |
| `InPast()` | DateTime in past |
| `InFuture()` | DateTime in future |
| `DateBetween(start, end)` | DateTime in range |
| `Count(expected)` | Collection count |
| `MinCount(min)` | Minimum items |
| `MaxCount(max)` | Maximum items |
| `NoNullItems()` | No null items in collection |
| `Must(predicate, message)` | Custom validation |

---

## 2. Result Pattern

Collect all validation errors instead of throwing on the first failure.

### Error Accumulation

```csharp
var result = GuardResult.Combine(
    Ensure.Accumulate(email, "Email").NotNull().Email().ToResult(),
    Ensure.Accumulate(password, "Password").MinLength(8).ToResult(),
    Ensure.Accumulate(age, "Age").InRange(18, 120).ToResult()
);

if (result.IsInvalid)
{
    Console.WriteLine($"Found {result.Errors.Count} error(s):");
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"  [{error.ParameterName}] {error.Message} (Code: {error.ErrorCode})");
    }
}
```

### API Response Format

```csharp
// Convert to dictionary for API responses
Dictionary<string, string[]> errors = result.ToErrorDictionary();

// Returns:
// {
//   "Email": ["Email must be a valid email address."],
//   "Password": ["Password must be at least 8 characters."]
// }
```

### GuardResult Methods

| Method | Description |
|--------|-------------|
| `IsValid` | True if no errors |
| `IsInvalid` | True if has errors |
| `Errors` | List of ValidationError |
| `ThrowIfInvalid()` | Throw AggregateValidationException if invalid |
| `GetErrorSummary()` | Formatted error string |
| `ToErrorDictionary()` | API-friendly dictionary |
| `Combine(results)` | Merge multiple results |
| `Merge(other)` | Combine with another result |

---

## 3. Async Validation

For validations that require async operations (database lookups, API calls, etc.).

```csharp
var result = await EnsureAsync.That(email, "Email")
    .UniqueAsync(async e => await db.IsEmailUniqueAsync(e))
    .ExistsAsync(async e => await emailService.IsDeliverableAsync(e))
    .MustAsync(async e => await IsNotBlacklistedAsync(e), "Email is blacklisted")
    .ValidateAsync();

if (result.IsValid)
{
    // Proceed with registration
}
```

### Combining Async Validations

```csharp
var result = await EnsureAsync.AllAsync(
    userValidator.ValidateAsync(user),
    addressValidator.ValidateAsync(address),
    paymentValidator.ValidateAsync(payment)
);
```

---

## 4. Conditional Validation

Apply validations only when certain conditions are met.

### When/Unless

```csharp
Ensure.That(alternateEmail)
    .When(isPrimaryEmailInvalid)    // Only validate when true
    .NotNull()
    .Email()
    .Unless(isGuestUser)            // Skip when true
    .MinLength(5);
```

### With Lambda Conditions

```csharp
Ensure.That(discountCode)
    .When(order => order.Total > 100)
    .NotEmpty()
    .Matches(@"^[A-Z0-9]{8}$");
```

### Reset Condition

```csharp
Ensure.That(value)
    .When(someCondition)
    .NotNull()          // Conditional
    .Always()           // Reset - always validate from here
    .NotEmpty();        // Always validated
```

---

## 5. Object Validation

Validate entire objects with property expressions.

```csharp
var result = Validate.Object(userDto)
    .Property(u => u.Email, g => g.NotNull().Email())
    .Property(u => u.Password, g => g.NotNull().MinLength(8))
    .Property(u => u.Age, g => g.InRange(18, 120))
    .NotNull(u => u.CreatedAt)
    .NotEmpty(u => u.Username)
    .Must(u => u.Role, r => ValidRoles.Contains(r), "Invalid role")
    .ToResult();

// Or throw on first error
var validUser = Validate.ObjectStrict(userDto)
    .Property(u => u.Email, g => g.NotNull().Email())
    .Build(); // Throws if invalid
```

---

## 6. Code Contracts

Design-by-contract programming with preconditions and postconditions.

```csharp
public decimal CalculateDiscount(decimal price, decimal discountPercent)
{
    // Preconditions - validate inputs
    Contract.Requires(price >= 0, "Price must be non-negative");
    Contract.Requires(discountPercent >= 0 && discountPercent <= 100, "Invalid discount percentage");
    Contract.RequiresNotNull(currency, nameof(currency));
    
    var result = price * (1 - discountPercent / 100);
    
    // Postcondition - validate output
    Contract.Ensures(result >= 0, "Result cannot be negative");
    Contract.Ensures(result <= price, "Discounted price cannot exceed original");
    
    return result;
}

public void ProcessOrder(Order order)
{
    // Invariant - always true condition
    Contract.Invariant(order.Items.Count > 0, "Order must have items");
}
```

### Debug-Only Guards

```csharp
// Only active in DEBUG builds - zero overhead in RELEASE
DebugGuard.Assert(value > 0, "Value should be positive");
DebugGuard.NotNull(connection, nameof(connection));
```

---

## 7. High-Performance Guards

Optimized for hot paths with aggressive inlining and span-based operations.

```csharp
// Aggressively inlined - minimal overhead
FastGuard.NotNull(user, nameof(user));
FastGuard.NotNullOrEmpty(email, nameof(email));
FastGuard.InRange(age, 0, 150, nameof(age));
FastGuard.Positive(quantity, nameof(quantity));

// Span-based for zero allocations
ReadOnlySpan<byte> data = GetData();
FastGuard.NotEmpty(data, nameof(data));

// Cached regex for repeated validations
FastGuard.Email(email, nameof(email));
```

### Regex Caching

```csharp
// Patterns are compiled and cached automatically
bool isValid = RegexCache.IsMatch(input, @"^\d{10}$");

// Manual cache management
RegexCache.Clear();
int cacheSize = RegexCache.CacheSize;
```

---

## 8. Logical Guards

Combine validations with AND/OR logic.

### OR Logic (Any Passes)

```csharp
// Contact must be either valid email OR valid phone
var contact = contactInfo.EitherOr("Contact")
    .Or(c => IsValidEmail(c), "valid email")
    .Or(c => IsValidPhone(c), "valid phone number")
    .Validate();
```

### AND Logic with Short-Circuit

```csharp
// All conditions must pass, stop on first failure
var result = input.AllOf("Input", shortCircuit: true)
    .And(v => v != null, "cannot be null")
    .And(v => v.Length > 0, "cannot be empty")
    .And(v => v.Length <= 100, "too long")
    .And(v => !v.Contains("<script>"), "no scripts allowed")
    .ToResult();
```

---

## 9. Advanced Validators

### Financial

```csharp
// Credit Card (Luhn algorithm)
"4111111111111111".AgainstInvalidCreditCard("cardNumber");
"5500000000000004".AgainstInvalidMasterCard("masterCard");
"4111111111111111".AgainstInvalidVisaCard("visaCard");

// IBAN
"DE89370400440532013000".AgainstInvalidIban("iban");
```

### National IDs

```csharp
// Turkish ID (TC Kimlik No)
"10000000146".AgainstInvalidTurkishId("tcKimlik");
```

### Data Formats

```csharp
// JSON
"{\"name\": \"test\"}".AgainstInvalidJson("jsonData");
"{\"items\": []}".AgainstInvalidJsonObject("jsonObject");

// XML
"<root><item/></root>".AgainstInvalidXml("xmlData");

// Base64
"SGVsbG8gV29ybGQ=".AgainstInvalidBase64("encoded");
```

### Other Formats

```csharp
// Phone (E.164)
"+905551234567".AgainstInvalidPhoneNumber("phone");
"05551234567".AgainstInvalidTurkishPhoneNumber("phone");

// Color
"#FF5733".AgainstInvalidHexColor("color");

// Version
"1.2.3-beta.1+build.456".AgainstInvalidSemVer("version");

// URL Slug
"my-article-title".AgainstInvalidSlug("slug");
```

---

## 10. Business Domain Guards

### Money & Currency

```csharp
price.AgainstInvalidMonetaryAmount("amount", maxDecimalPlaces: 2);
"USD".AgainstInvalidCurrencyCode("currency");
discount.AgainstInvalidPercentage("discount", allowOver100: false);
```

### E-commerce

```csharp
quantity.AgainstInvalidQuantity("qty", minQuantity: 1);
"PROD-12345-XL".AgainstInvalidSku("sku");
total.AgainstOrderBelowMinimum(50m, "orderTotal");
discount.AgainstInvalidDiscount("discount", maxDiscount: 50m);
"SUMMER2025".AgainstInvalidCouponCode("coupon");
```

### Business Hours

```csharp
orderDate.AgainstWeekend("deliveryDate");
appointmentTime.AgainstOutsideBusinessHours("time", startHour: 9, endHour: 17);
(startDate, endDate).AgainstInvalidDateRange("period");
subscriptionMonths.AgainstInvalidSubscriptionPeriod("duration");
```

### User & Account

```csharp
age.AgainstInvalidAccountAge("age", minAge: 13, maxAge: 120);
role.AgainstInvalidRole("role", "User", "Admin", "Moderator");
currentStatus.AgainstInvalidStatusTransition(newStatus, validTransitions, "status");
```

### Rating & Review

```csharp
rating.AgainstInvalidRating("stars", minRating: 1, maxRating: 5);
reviewText.AgainstInvalidReviewText("review", minLength: 10, maxLength: 5000);
```

---

## 11. Attribute-Based Validation

Decorate classes with validation attributes.

### Define Model

```csharp
public class CreateUserRequest
{
    [NotNull]
    [Email(ErrorMessage = "Please provide a valid email")]
    public string Email { get; set; }
    
    [NotEmpty]
    [Length(8, 128, ErrorCode = "PASS_LENGTH")]
    public string Password { get; set; }
    
    [Range(18, 120)]
    [Positive]
    public int Age { get; set; }
    
    [Regex(@"^\+?[1-9]\d{1,14}$", ErrorMessage = "Invalid phone format")]
    public string? Phone { get; set; }
}
```

### Validate

```csharp
var result = AttributeValidator.Validate(request);

if (result.IsInvalid)
{
    return BadRequest(result.ToErrorDictionary());
}

// Or throw directly
var validRequest = AttributeValidator.ValidateAndThrow(request);
```

### Available Attributes

| Attribute | Description |
|-----------|-------------|
| `[NotNull]` | Value cannot be null |
| `[NotEmpty]` | String/Collection cannot be empty |
| `[Length(min, max)]` | String length range |
| `[Email]` | Valid email format |
| `[Range(min, max)]` | Numeric range |
| `[Regex(pattern)]` | Pattern match |
| `[Positive]` | Value must be positive |

---

## 12. Dependency Injection

### Registration

```csharp
// In Program.cs or Startup.cs
services.AddOrionGuard();

// Register custom validators
services.AddValidator<CreateUserRequest, CreateUserRequestValidator>();
```

### Create Validator

```csharp
public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator(IUserRepository userRepo)
    {
        RuleFor(x => x.Email, "Email", v => v.NotNull().NotEmpty().Email());
        RuleFor(x => x.Password, "Password", v => v.NotNull().MinLength(8));
        
        // Async validation
        RuleForAsync(
            async x => await userRepo.IsEmailUniqueAsync(x.Email),
            "Email already registered",
            "Email"
        );
        
        // Custom rule
        RuleFor(
            x => x.Password != x.Email,
            "Password cannot be same as email",
            "Password"
        );
    }
}
```

### Use in Controller

```csharp
public class UsersController : ControllerBase
{
    private readonly IValidator<CreateUserRequest> _validator;
    
    public UsersController(IValidator<CreateUserRequest> validator)
    {
        _validator = validator;
    }
    
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var result = await _validator.ValidateAsync(request);
        
        if (result.IsInvalid)
        {
            return BadRequest(result.ToErrorDictionary());
        }
        
        // Create user...
        return Ok();
    }
}
```

---

## 13. Localization

### Set Culture

```csharp
// Globally
ValidationMessages.SetCulture("tr");

// Or with CultureInfo
ValidationMessages.SetCulture(new CultureInfo("tr-TR"));
```

### Get Messages

```csharp
var message = ValidationMessages.Get("Email", "E-posta");
// TR: "E-posta ge�erli bir e-posta adresi olmal?d?r."
// EN: "E-posta must be a valid email address."
```

### Supported Languages

| Language | Code |
|----------|------|
| English | `en` |
| Turkish | `tr` |
| German | `de` |
| French | `fr` |

### Add Custom Translations

```csharp
ValidationMessages.AddMessages("es", new Dictionary<string, string>
{
    ["NotNull"] = "{0} no puede ser nulo.",
    ["NotEmpty"] = "{0} no puede estar vac�o.",
    ["Email"] = "{0} debe ser una direcci�n de correo v�lida.",
    ["MinLength"] = "{0} debe tener al menos {1} caracteres.",
    ["InRange"] = "{0} debe estar entre {1} y {2}."
});
```

### Custom Resolver

```csharp
// Use your own localization system
ValidationMessages.SetMessageResolver((key, culture) =>
{
    return _localizer[key].Value; // ASP.NET Core IStringLocalizer
});
```

---

## 14. Common Profiles

Pre-built validation for common scenarios.

### Authentication

```csharp
var emailResult = CommonProfiles.Email("user@example.com");

var passwordResult = CommonProfiles.Password("SecureP@ss1",
    minLength: 8,
    maxLength: 128,
    requireUppercase: true,
    requireLowercase: true,
    requireDigit: true,
    requireSpecialChar: true);

var usernameResult = CommonProfiles.Username("john_doe",
    minLength: 3,
    maxLength: 30);
```

### Personal Information

```csharp
var nameResult = CommonProfiles.PersonName("John Doe", minLength: 2, maxLength: 100);
var ageResult = CommonProfiles.Age(25, minAge: 18, maxAge: 120);
var birthDateResult = CommonProfiles.BirthDate(birthDate, minAge: 18);
```

### Contact

```csharp
var phoneResult = CommonProfiles.PhoneNumber("+1234567890");
var urlResult = CommonProfiles.Url("https://example.com");
```

### Financial

```csharp
var amountResult = CommonProfiles.MonetaryAmount(99.99m, min: 0, max: 10000);
var percentResult = CommonProfiles.Percentage(15.5m);
```

### Identifiers

```csharp
var guidResult = CommonProfiles.GuidId(userId);
var intIdResult = CommonProfiles.IntegerId(productId);
var slugResult = CommonProfiles.Slug("my-article-title");
```

### Collections

```csharp
var listResult = CommonProfiles.NonEmptyList(items);
var countedListResult = CommonProfiles.ListWithCount(items, minCount: 1, maxCount: 100);
```

---

## Quick Reference Card

```csharp
// ? Simple validation
Ensure.That(email).NotNull().Email();

// ? Get all errors
var result = Ensure.Accumulate(email, "Email").Email().ToResult();

// ? Async validation
await EnsureAsync.That(email, "Email").UniqueAsync(checkUnique).ValidateAsync();

// ? Conditional
Ensure.That(value).When(condition).NotNull();

// ? Object validation
Validate.Object(dto).Property(x => x.Email, g => g.Email()).ToResult();

// ? Contracts
Contract.Requires(value > 0, "Must be positive");

// ? Fast path
FastGuard.NotNull(value, nameof(value));

// ? OR logic
value.EitherOr("value").Or(v => test1(v), "msg1").Or(v => test2(v), "msg2").Validate();

// ? Attributes
[NotNull, Email] public string Email { get; set; }

// ? Profiles
CommonProfiles.Email("user@example.com");
```

---

## Need Help?

- ?? [Full Documentation](https://github.com/tunahanaliozturk/OrionGuard)
- ?? [Report Issues](https://github.com/tunahanaliozturk/OrionGuard/issues)
- ?? [Discussions](https://github.com/tunahanaliozturk/OrionGuard/discussions)

---

**Made with ?? by Tunahan Ali Ozturk**
