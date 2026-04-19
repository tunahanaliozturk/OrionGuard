using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

public class PolymorphicValidatorTests
{
    #region Test DTOs

    private abstract class PaymentBase
    {
        public decimal Amount { get; set; }
    }

    private sealed class CreditCardPayment : PaymentBase
    {
        public string? CardNumber { get; set; }
    }

    private sealed class BankTransferPayment : PaymentBase
    {
        public string? Iban { get; set; }
    }

    private sealed class CryptoPayment : PaymentBase
    {
        public string? WalletAddress { get; set; }
    }

    #endregion

    #region When<TDerived>

    [Fact]
    public void When_ShouldMatchCorrectDerivedType()
    {
        var validator = Validate.Polymorphic<PaymentBase>()
            .When<CreditCardPayment>(cc =>
            {
                if (string.IsNullOrWhiteSpace(cc.CardNumber))
                    return GuardResult.Failure("CardNumber", "Card number is required.");
                return GuardResult.Success();
            })
            .When<BankTransferPayment>(bt =>
            {
                if (string.IsNullOrWhiteSpace(bt.Iban))
                    return GuardResult.Failure("Iban", "IBAN is required.");
                return GuardResult.Success();
            });

        var ccResult = validator.Validate(new CreditCardPayment { Amount = 100, CardNumber = null });
        Assert.True(ccResult.IsInvalid);
        Assert.Equal("CardNumber", ccResult.Errors[0].ParameterName);

        var btResult = validator.Validate(new BankTransferPayment { Amount = 200, Iban = "DE89370400440532013000" });
        Assert.True(btResult.IsValid);
    }

    [Fact]
    public void When_ShouldPass_WhenDerivedTypeIsValid()
    {
        var validator = Validate.Polymorphic<PaymentBase>()
            .When<CreditCardPayment>(cc =>
            {
                if (string.IsNullOrWhiteSpace(cc.CardNumber))
                    return GuardResult.Failure("CardNumber", "Card number is required.");
                return GuardResult.Success();
            });

        var result = validator.Validate(new CreditCardPayment { CardNumber = "4111111111111111" });
        Assert.True(result.IsValid);
    }

    #endregion

    #region Otherwise Fallback

    [Fact]
    public void Otherwise_ShouldBeUsed_WhenNoSpecificValidatorRegistered()
    {
        var validator = Validate.Polymorphic<PaymentBase>()
            .When<CreditCardPayment>(_ => GuardResult.Success())
            .Otherwise(p =>
            {
                if (p.Amount <= 0)
                    return GuardResult.Failure("Amount", "Amount must be positive.");
                return GuardResult.Success();
            });

        var result = validator.Validate(new CryptoPayment { Amount = 0 });
        Assert.True(result.IsInvalid);
        Assert.Equal("Amount", result.Errors[0].ParameterName);
    }

    [Fact]
    public void Otherwise_ShouldNotBeUsed_WhenSpecificValidatorExists()
    {
        var fallbackCalled = false;
        var validator = Validate.Polymorphic<PaymentBase>()
            .When<CreditCardPayment>(_ => GuardResult.Success())
            .Otherwise(_ =>
            {
                fallbackCalled = true;
                return GuardResult.Failure("test", "fallback");
            });

        validator.Validate(new CreditCardPayment { CardNumber = "4111" });
        Assert.False(fallbackCalled);
    }

    #endregion

    #region Unregistered Type

    [Fact]
    public void Validate_ShouldReturnSuccess_WhenTypeNotRegisteredAndNoFallback()
    {
        var validator = Validate.Polymorphic<PaymentBase>()
            .When<CreditCardPayment>(_ => GuardResult.Success());

        var result = validator.Validate(new CryptoPayment { Amount = 100 });
        Assert.True(result.IsValid);
    }

    #endregion

    #region ValidateAndThrow

    [Fact]
    public void ValidateAndThrow_ShouldReturnInstance_WhenValid()
    {
        var payment = new CreditCardPayment { CardNumber = "4111111111111111", Amount = 50 };

        var validator = Validate.Polymorphic<PaymentBase>()
            .When<CreditCardPayment>(_ => GuardResult.Success());

        var returned = validator.ValidateAndThrow(payment);
        Assert.Same(payment, returned);
    }

    [Fact]
    public void ValidateAndThrow_ShouldThrow_WhenInvalid()
    {
        var validator = Validate.Polymorphic<PaymentBase>()
            .When<CreditCardPayment>(cc =>
            {
                if (string.IsNullOrWhiteSpace(cc.CardNumber))
                    return GuardResult.Failure("CardNumber", "Required.");
                return GuardResult.Success();
            });

        Assert.Throws<AggregateValidationException>(() =>
            validator.ValidateAndThrow(new CreditCardPayment { CardNumber = null }));
    }

    [Fact]
    public void Validate_ShouldThrow_WhenInstanceIsNull()
    {
        var validator = Validate.Polymorphic<PaymentBase>();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
    }

    #endregion
}
