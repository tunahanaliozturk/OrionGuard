using FluentValidation;
using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Compatibility;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Generators;
using OG = Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Benchmarks.Comparison;

// Shared models and validators for the OrionGuard vs FluentValidation comparison.
// Every scenario validates the same inputs against the same rules in each library,
// written the way each library's documentation writes them. See benchmarks.md for the fairness rules.

/// <summary>
/// Rule parameters and predicates shared by both libraries, so each side evaluates the same logic.
/// </summary>
public static class SharedRules
{
    public const int NicknameMaxLength = 20;
    public const int MinAge = 18;
    public const int MaxAge = 120;
    public const int PostalCodeMin = 5;
    public const int PostalCodeMax = 10;
    public const int MinQuantity = 1;
    public const int MaxQuantity = 100;
    public const string AvailableEmail = "alice@example.com";

    private static readonly Task<bool> Available = Task.FromResult(true);
    private static readonly Task<bool> Taken = Task.FromResult(false);

    public static bool IsSupportedCountry(string? code) => code is "TR" or "US" or "DE" or "GB" or "FR";

    /// <summary>
    /// Stands in for an I/O lookup (for example a uniqueness query). It completes synchronously with a
    /// cached task, so the async benchmarks measure each library's async pipeline, not I/O or the thread pool.
    /// </summary>
    public static Task<bool> IsEmailAvailableAsync(string? email, CancellationToken cancellationToken) =>
        email == AvailableEmail ? Available : Taken;
}

/// <summary>
/// Scenario (a) model: 5 properties, one rule each (NotEmpty, MaximumLength, EmailAddress, InclusiveBetween, Must).
/// The OrionGuard attributes only drive the <c>[GenerateValidator]</c> source generator; FluentValidation ignores them.
/// </summary>
[GenerateValidator]
public sealed class CustomerDto
{
    [NotEmpty]
    public string? Name { get; set; }

    [Length(0, SharedRules.NicknameMaxLength)]
    public string? Nickname { get; set; }

    [Email]
    public string? Email { get; set; }

    [Range(SharedRules.MinAge, SharedRules.MaxAge)]
    public int Age { get; set; }

#pragma warning disable OG0001 // No attribute exists for a custom predicate; the Must rule is applied by hand next to the generated validator.
    public string? CountryCode { get; set; }
#pragma warning restore OG0001

    public static CustomerDto Valid() => new()
    {
        Name = "Alice",
        Nickname = "ally",
        Email = SharedRules.AvailableEmail,
        Age = 30,
        CountryCode = "TR"
    };

    /// <summary>Every one of the 5 rules fails, in every implementation.</summary>
    public static CustomerDto Invalid() => new()
    {
        Name = "",
        Nickname = "this-nickname-is-far-too-long",
        Email = "not-an-email",
        Age = 7,
        CountryCode = "XX"
    };
}

/// <summary>Scenario (b) model: a root with a nested object and a collection of child items.</summary>
public sealed class OrderDto
{
    public string? OrderNumber { get; set; }
    public AddressDto ShippingAddress { get; set; } = new();
    public List<OrderLineDto> Lines { get; set; } = new();

    public static OrderDto Create(bool valid, int lineCount) => new()
    {
        OrderNumber = valid ? "ORD-2026-0001" : "",
        ShippingAddress = new AddressDto
        {
            City = valid ? "Istanbul" : "",
            PostalCode = valid ? "34000" : "1"
        },
        Lines = Enumerable.Range(0, lineCount)
            .Select(i => new OrderLineDto { Sku = valid ? $"SKU-{i:D4}" : "", Quantity = valid ? 1 + i : 0 })
            .ToList()
    };
}

public sealed class AddressDto
{
    public string? City { get; set; }
    public string? PostalCode { get; set; }
}

public sealed class OrderLineDto
{
    public string? Sku { get; set; }
    public int Quantity { get; set; }
}

// ---------------------------------------------------------------------------------------------
// FluentValidation validators
// ---------------------------------------------------------------------------------------------

public class FvCustomerValidator : AbstractValidator<CustomerDto>
{
    public FvCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Nickname).MaximumLength(SharedRules.NicknameMaxLength);
        RuleFor(x => x.Email).EmailAddress();
        RuleFor(x => x.Age).InclusiveBetween(SharedRules.MinAge, SharedRules.MaxAge);
        RuleFor(x => x.CountryCode).Must(SharedRules.IsSupportedCountry);
    }
}

/// <summary>Scenario (c): FluentValidation's fail-fast switch. Stops at the first failing rule.</summary>
public sealed class FvCustomerStopValidator : FvCustomerValidator
{
    public FvCustomerStopValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
    }
}

/// <summary>Scenario (e): the 5 sync rules plus one async rule.</summary>
public sealed class FvCustomerAsyncValidator : FvCustomerValidator
{
    public FvCustomerAsyncValidator()
    {
        RuleFor(x => x.Email).MustAsync(SharedRules.IsEmailAvailableAsync);
    }
}

public sealed class FvAddressValidator : AbstractValidator<AddressDto>
{
    public FvAddressValidator()
    {
        RuleFor(x => x.City).NotEmpty();
        RuleFor(x => x.PostalCode).Length(SharedRules.PostalCodeMin, SharedRules.PostalCodeMax);
    }
}

public sealed class FvOrderLineValidator : AbstractValidator<OrderLineDto>
{
    public FvOrderLineValidator()
    {
        RuleFor(x => x.Sku).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(SharedRules.MinQuantity, SharedRules.MaxQuantity);
    }
}

public sealed class FvOrderValidator : AbstractValidator<OrderDto>
{
    public FvOrderValidator()
    {
        RuleFor(x => x.OrderNumber).NotEmpty();
        RuleFor(x => x.ShippingAddress).NotNull().SetValidator(new FvAddressValidator());
        RuleForEach(x => x.Lines).SetValidator(new FvOrderLineValidator());
    }
}

// ---------------------------------------------------------------------------------------------
// OrionGuard: reusable validator (OrionGuard.DependencyInjection.AbstractValidator<T>)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// OrionGuard's reusable, DI-registered validator. Its property builder has no MaximumLength or
/// InclusiveBetween, so those rules use Length(0, max) and Must, which is how its API expresses them.
/// FluentValidation's NotEmpty also rejects null, hence NotNull().NotEmpty() here.
/// </summary>
public class OgCustomerValidator : OG.AbstractValidator<CustomerDto>
{
    public OgCustomerValidator()
    {
        RuleFor(x => x.Name, nameof(CustomerDto.Name), p => p.NotNull().NotEmpty());
        RuleFor(x => x.Nickname, nameof(CustomerDto.Nickname), p => p.Length(0, SharedRules.NicknameMaxLength));
        RuleFor(x => x.Email, nameof(CustomerDto.Email), p => p.Email());
        RuleFor(x => x.Age, nameof(CustomerDto.Age),
            p => p.Must(age => age is >= SharedRules.MinAge and <= SharedRules.MaxAge, "Age must be between 18 and 120."));
        RuleFor(x => x.CountryCode, nameof(CustomerDto.CountryCode),
            p => p.Must(SharedRules.IsSupportedCountry, "Country is not supported."));
    }
}

/// <summary>Scenario (e): the 5 sync rules plus one async rule.</summary>
public sealed class OgCustomerAsyncValidator : OgCustomerValidator
{
    public OgCustomerAsyncValidator()
    {
        // RuleForAsync has no CancellationToken parameter, so the predicate cannot observe one.
        RuleForAsync(x => SharedRules.IsEmailAvailableAsync(x.Email, CancellationToken.None),
            "Email is already registered.", nameof(CustomerDto.Email));
    }
}

// ---------------------------------------------------------------------------------------------
// OrionGuard: FluentValidation compatibility layer (FluentStyleValidator<T>)
// ---------------------------------------------------------------------------------------------

/// <summary>The FluentValidation validator above, migrated by swapping the base class only.</summary>
public sealed class OgCompatCustomerValidator : FluentStyleValidator<CustomerDto>
{
    public OgCompatCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Nickname).MaximumLength(SharedRules.NicknameMaxLength);
        RuleFor(x => x.Email).EmailAddress();
        RuleFor(x => x.Age).InclusiveBetween(SharedRules.MinAge, SharedRules.MaxAge);
        RuleFor(x => x.CountryCode).Must(SharedRules.IsSupportedCountry);
    }
}

// ---------------------------------------------------------------------------------------------
// OrionGuard: inline fluent validation (Validate.For / Validate.ForStrict / Validate.Nested)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// OrionGuard's inline API binds to one instance and runs rules eagerly as the chain executes, so there
/// is no validator object to create once and reuse: the chain below is the per-call cost by design.
/// </summary>
public static class OgInline
{
    public static GuardResult ValidateCustomer(CustomerDto dto) =>
        Validate.For(dto)
            .NotEmpty(x => x.Name)
            .Property(x => x.Nickname, g => g.MaxLength(SharedRules.NicknameMaxLength))
            .Property(x => x.Email, g => g.Email())
            .Property(x => x.Age, g => g.InRange(SharedRules.MinAge, SharedRules.MaxAge))
            .Must(x => x.CountryCode, SharedRules.IsSupportedCountry, "Country is not supported.")
            .ToResult();

    /// <summary>Fail-fast: throws <see cref="AggregateValidationException"/> at the first failing rule.</summary>
    public static CustomerDto ValidateCustomerStrict(CustomerDto dto) =>
        Validate.ForStrict(dto)
            .NotEmpty(x => x.Name)
            .Property(x => x.Nickname, g => g.MaxLength(SharedRules.NicknameMaxLength))
            .Property(x => x.Email, g => g.Email())
            .Property(x => x.Age, g => g.InRange(SharedRules.MinAge, SharedRules.MaxAge))
            .Must(x => x.CountryCode, SharedRules.IsSupportedCountry, "Country is not supported.")
            .ThrowIfInvalid();

    public static Task<GuardResult> ValidateCustomerAsync(CustomerDto dto, CancellationToken cancellationToken) =>
        Validate.For(dto)
            .NotEmpty(x => x.Name)
            .Property(x => x.Nickname, g => g.MaxLength(SharedRules.NicknameMaxLength))
            .Property(x => x.Email, g => g.Email())
            .Property(x => x.Age, g => g.InRange(SharedRules.MinAge, SharedRules.MaxAge))
            .Must(x => x.CountryCode, SharedRules.IsSupportedCountry, "Country is not supported.")
            .MustAsync(x => x.Email, SharedRules.IsEmailAvailableAsync, "Email is already registered.")
            .ToResultAsync(cancellationToken);

    public static GuardResult ValidateOrder(OrderDto order) =>
        Validate.Nested(order)
            .Property(x => x.OrderNumber, p => p.NotEmpty())
            .Nested(x => x.ShippingAddress, address => address
                .Property(a => a.City, p => p.NotEmpty())
                .Property(a => a.PostalCode, p => p.Length(SharedRules.PostalCodeMin, SharedRules.PostalCodeMax)))
            .Collection(x => x.Lines, (line, _) => line
                .Property(l => l.Sku, p => p.NotEmpty())
                .Property(l => l.Quantity, p => p.InRange(SharedRules.MinQuantity, SharedRules.MaxQuantity)))
            .ToResult();

    /// <summary>
    /// The <c>[GenerateValidator]</c> path. The generator covers the 4 attribute rules; there is no attribute
    /// for a custom predicate, so the Must rule is checked by hand and merged, as a caller would have to.
    /// </summary>
    public static GuardResult ValidateCustomerGenerated(CustomerDto dto)
    {
        var result = CustomerDtoValidator.Validate(dto);
        return SharedRules.IsSupportedCountry(dto.CountryCode)
            ? result
            : result.Merge(GuardResult.Failure(nameof(CustomerDto.CountryCode), "Country is not supported.", "PREDICATE"));
    }
}

/// <summary>
/// Fairness guard run from each benchmark's [GlobalSetup], outside the measurement: every implementation
/// must report the same number of errors for the same input, or the run aborts.
/// </summary>
public static class Parity
{
    public static void Expect(string implementation, int actual, int expected)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"{implementation} reported {actual} error(s) but {expected} were expected. The comparison would not be like-for-like.");
        }
    }
}
