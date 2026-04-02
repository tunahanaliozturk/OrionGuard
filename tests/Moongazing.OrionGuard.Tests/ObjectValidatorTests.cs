using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

public class ObjectValidatorTests
{
    private sealed class Person
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public int Age { get; set; }
        public DateTime BirthDate { get; set; }
        public string? Password { get; set; }
        public string? PasswordConfirm { get; set; }
    }

    #region Basic Validation

    [Fact]
    public void For_ShouldReturnValidator_WhenInstanceIsNotNull()
    {
        var person = new Person { Name = "Test" };
        var validator = Validate.For(person);
        Assert.NotNull(validator);
    }

    [Fact]
    public void For_ShouldThrowArgumentNullException_WhenInstanceIsNull()
    {
        Person? person = null;
        Assert.Throws<ArgumentNullException>(() => Validate.For(person!));
    }

    [Fact]
    public void Property_ShouldAccumulate_MultipleErrors()
    {
        var person = new Person { Name = null, Age = -5 };

        var result = Validate.For(person)
            .NotNull(p => p.Name)
            .Must(p => p.Age, age => age > 0, "Age must be positive")
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void Property_ShouldReturnValid_WhenAllPropertiesAreValid()
    {
        var person = new Person { Name = "Tunahan", Age = 30 };

        var result = Validate.For(person)
            .NotNull(p => p.Name)
            .Must(p => p.Age, age => age > 0, "Age must be positive")
            .ToResult();

        Assert.True(result.IsValid);
    }

    #endregion

    #region CrossProperty Validation

    [Fact]
    public void CrossProperty_ShouldFail_WhenPredicateReturnsFalse()
    {
        var person = new Person
        {
            Password = "abc123",
            PasswordConfirm = "different"
        };

        var result = Validate.For(person)
            .CrossProperty(
                p => p.Password,
                p => p.PasswordConfirm,
                (pass, confirm) => pass == confirm,
                "Passwords must match")
            .ToResult();

        Assert.True(result.IsInvalid);
        Assert.Contains(result.Errors, e => e.Message == "Passwords must match");
        Assert.Contains(result.Errors, e => e.ErrorCode == "CROSS_PROPERTY");
    }

    [Fact]
    public void CrossProperty_ShouldPass_WhenPredicateReturnsTrue()
    {
        var person = new Person
        {
            Password = "abc123",
            PasswordConfirm = "abc123"
        };

        var result = Validate.For(person)
            .CrossProperty(
                p => p.Password,
                p => p.PasswordConfirm,
                (pass, confirm) => pass == confirm,
                "Passwords must match")
            .ToResult();

        Assert.True(result.IsValid);
    }

    #endregion

    #region When (Conditional Validation)

    [Fact]
    public void When_ShouldApplyRules_WhenConditionIsTrue()
    {
        var person = new Person { Name = null, Age = 25 };

        var result = Validate.For(person)
            .When(true, v => v.NotNull(p => p.Name))
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void When_ShouldSkipRules_WhenConditionIsFalse()
    {
        var person = new Person { Name = null, Age = 25 };

        var result = Validate.For(person)
            .When(false, v => v.NotNull(p => p.Name))
            .ToResult();

        Assert.True(result.IsValid);
    }

    #endregion

    #region ForStrict (Throw on First Error)

    [Fact]
    public void ForStrict_ShouldThrowOnFirstError()
    {
        var person = new Person { Name = null, Age = -5 };

        Assert.Throws<AggregateValidationException>(() =>
            Validate.ForStrict(person)
                .NotNull(p => p.Name)
                .Must(p => p.Age, age => age > 0, "Age must be positive")
                .Build());
    }

    #endregion

    #region ThrowIfInvalid / Build

    [Fact]
    public void ThrowIfInvalid_ShouldReturnInstance_WhenValid()
    {
        var person = new Person { Name = "Test", Age = 25 };

        var result = Validate.For(person)
            .NotNull(p => p.Name)
            .Must(p => p.Age, age => age > 0, "Age must be positive")
            .Build();

        Assert.Same(person, result);
    }

    [Fact]
    public void ThrowIfInvalid_ShouldThrow_WhenInvalid()
    {
        var person = new Person { Name = null };

        Assert.Throws<AggregateValidationException>(() =>
            Validate.For(person)
                .NotNull(p => p.Name)
                .ThrowIfInvalid());
    }

    #endregion

    #region Validate.All (Combine Results)

    [Fact]
    public void All_ShouldCombineMultipleResults()
    {
        var result1 = GuardResult.Success();
        var result2 = GuardResult.Failure("field", "error message");

        var combined = Validate.All(result1, result2);

        Assert.True(combined.IsInvalid);
        Assert.Single(combined.Errors);
    }

    [Fact]
    public void All_ShouldReturnSuccess_WhenAllResultsAreValid()
    {
        var result1 = GuardResult.Success();
        var result2 = GuardResult.Success();

        var combined = Validate.All(result1, result2);

        Assert.True(combined.IsValid);
    }

    #endregion

    #region NotEmpty

    [Fact]
    public void NotEmpty_ShouldFail_WhenStringPropertyIsEmpty()
    {
        var person = new Person { Name = "  " };

        var result = Validate.For(person)
            .NotEmpty(p => p.Name)
            .ToResult();

        Assert.True(result.IsInvalid);
    }

    #endregion
}
