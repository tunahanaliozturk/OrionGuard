using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Demo.Domain;

// Abstract base class style — for behavior-rich value objects.
// Override GetEqualityComponents() to declare what makes two instances equal.
public sealed class Money : ValueObject
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Ensure.That(amount).NotNegative();
        Ensure.That(currency).NotNull().NotEmpty().Length(3, 3);
        Amount = amount;
        Currency = currency;
    }

    public Money Add(Money other)
    {
        Ensure.That(other).NotNull();
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot add {Currency} and {other.Currency} — currencies must match.");
        }
        return new Money(Amount + other.Amount, Currency);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}

// Record-based style — pure data value object. Compiler synthesises equality for free.
// Explicit ctor runs the invariant check at every construction site.
public sealed record Address : IValueObject
{
    public string Street { get; }
    public string City { get; }
    public string PostalCode { get; }
    public string Country { get; }

    public Address(string street, string city, string postalCode, string country)
    {
        Ensure.That(street).NotNull().NotEmpty();
        Ensure.That(city).NotNull().NotEmpty();
        Ensure.That(postalCode).NotNull().Length(3, 12);
        Ensure.That(country).NotNull().Length(2, 2);
        Street = street;
        City = city;
        PostalCode = postalCode;
        Country = country;
    }
}
