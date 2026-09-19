using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.MassTransit.Tests;

public sealed class MessageValidationExceptionTests
{
    [Fact]
    public void Carries_Errors_And_ListsThemInToString()
    {
        var errors = new[]
        {
            new ValidationError("OrderId", "OrderId cannot be empty."),
            new ValidationError("CustomerEmail", "CustomerEmail must be a valid email."),
        };

        var exception = new MessageValidationException(errors, typeof(SubmitOrder));

        Assert.Equal(errors, exception.Errors);
        Assert.Equal(
            $"Message validation failed for '{typeof(SubmitOrder).FullName}' with 2 error(s).",
            exception.Message);
        Assert.Contains("  - [OrderId]: OrderId cannot be empty.", exception.ToString());
        Assert.Contains("  - [CustomerEmail]: CustomerEmail must be a valid email.", exception.ToString());
    }

    [Fact]
    public void NullErrors_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MessageValidationException(null!));
    }
}
