using System.Globalization;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DynamicRules;

namespace Moongazing.OrionGuard.Tests;

public class DynamicRuleEngineTests
{
    #region Test DTOs

    private sealed class UserDto
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public int Age { get; set; }
        public string? Role { get; set; }
        public string? Department { get; set; }
    }

    #endregion

    #region FromJson

    [Fact]
    public void FromJson_ShouldCreateValidator_WhenValidJson()
    {
        var json = """
        {
            "Name": "TestRules",
            "Rules": [
                { "PropertyName": "Name", "RuleType": "NotNull" }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        Assert.NotNull(validator);
    }

    [Fact]
    public void FromJson_ShouldThrow_WhenJsonIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => DynamicValidator.FromJson(""));
    }

    #endregion

    #region NotNull Rule

    [Fact]
    public void Validate_NotNull_ShouldFail_WhenPropertyIsNull()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                { "PropertyName": "Name", "RuleType": "NotNull" }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Name = null });

        Assert.True(result.IsInvalid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Validate_NotNull_ShouldPass_WhenPropertyHasValue()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                { "PropertyName": "Name", "RuleType": "NotNull" }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Name = "Alice" });

        Assert.True(result.IsValid);
    }

    #endregion

    #region NotEmpty Rule

    [Fact]
    public void Validate_NotEmpty_ShouldFail_WhenStringIsWhitespace()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                { "PropertyName": "Name", "RuleType": "NotEmpty" }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Name = "   " });

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Validate_NotEmpty_ShouldPass_WhenStringHasContent()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                { "PropertyName": "Name", "RuleType": "NotEmpty" }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Name = "Alice" });

        Assert.True(result.IsValid);
    }

    #endregion

    #region Length Rule

    [Fact]
    public void Validate_Length_ShouldFail_WhenTooShort()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Name",
                    "RuleType": "Length",
                    "Parameters": { "Min": 5, "Max": 50 }
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Name = "AB" });

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Validate_Length_ShouldFail_WhenTooLong()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Name",
                    "RuleType": "Length",
                    "Parameters": { "Min": 1, "Max": 5 }
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Name = "This is way too long" });

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Validate_Length_ShouldPass_WhenWithinBounds()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Name",
                    "RuleType": "Length",
                    "Parameters": { "Min": 1, "Max": 50 }
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Name = "Alice" });

        Assert.True(result.IsValid);
    }

    #endregion

    #region Range Rule

    [Fact]
    public void Validate_Range_ShouldFail_WhenBelowMin()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Age",
                    "RuleType": "Range",
                    "Parameters": { "Min": 18, "Max": 120 }
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Age = 10 });

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Validate_Range_ShouldFail_WhenAboveMax()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Age",
                    "RuleType": "Range",
                    "Parameters": { "Min": 18, "Max": 120 }
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Age = 200 });

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Validate_Range_ShouldPass_WhenWithinRange()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Age",
                    "RuleType": "Range",
                    "Parameters": { "Min": 18, "Max": 120 }
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Age = 30 });

        Assert.True(result.IsValid);
    }

    #endregion

    #region Email Rule

    [Fact]
    public void Validate_Email_ShouldFail_WhenInvalidFormat()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                { "PropertyName": "Email", "RuleType": "Email" }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Email = "not-an-email" });

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Validate_Email_ShouldPass_WhenValidFormat()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                { "PropertyName": "Email", "RuleType": "Email" }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Email = "alice@example.com" });

        Assert.True(result.IsValid);
    }

    #endregion

    #region In / NotIn Rules

    [Fact]
    public void Validate_In_ShouldFail_WhenValueNotInAllowedSet()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Role",
                    "RuleType": "In",
                    "Parameters": { "Values": ["Admin", "User", "Moderator"] }
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Role = "SuperUser" });

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Validate_In_ShouldPass_WhenValueInAllowedSet()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Role",
                    "RuleType": "In",
                    "Parameters": { "Values": ["Admin", "User", "Moderator"] }
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Role = "Admin" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NotIn_ShouldFail_WhenValueInForbiddenSet()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Role",
                    "RuleType": "NotIn",
                    "Parameters": { "Values": ["Banned", "Suspended"] }
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Role = "Banned" });

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Validate_NotIn_ShouldPass_WhenValueNotInForbiddenSet()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Role",
                    "RuleType": "NotIn",
                    "Parameters": { "Values": ["Banned", "Suspended"] }
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Role = "Admin" });

        Assert.True(result.IsValid);
    }

    #endregion

    #region Conditional Rules (WhenProperty)

    [Fact]
    public void Validate_ConditionalRule_ShouldApply_WhenConditionMet()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Department",
                    "RuleType": "NotEmpty",
                    "WhenProperty": "Role",
                    "WhenValue": "Admin"
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Role = "Admin", Department = null });

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Validate_ConditionalRule_ShouldSkip_WhenConditionNotMet()
    {
        var json = """
        {
            "Name": "Test",
            "Rules": [
                {
                    "PropertyName": "Department",
                    "RuleType": "NotEmpty",
                    "WhenProperty": "Role",
                    "WhenValue": "Admin"
                }
            ]
        }
        """;

        var validator = DynamicValidator.FromJson(json);
        var result = validator.Validate(new UserDto { Role = "User", Department = null });

        Assert.True(result.IsValid);
    }

    #endregion

    #region DynamicValidatorFactory

    [Fact]
    public void Factory_Register_ShouldAllowGetByName()
    {
        var factory = new DynamicValidatorFactory();
        var ruleSet = new DynamicRuleSet
        {
            Name = "UserRules",
            Rules = new List<DynamicRule>
            {
                new() { PropertyName = "Name", RuleType = "NotNull" }
            }
        };

        factory.Register("UserRules", ruleSet);

        var validator = factory.Get("UserRules");
        Assert.NotNull(validator);
    }

    [Fact]
    public void Factory_Get_ShouldThrow_WhenNameNotRegistered()
    {
        var factory = new DynamicValidatorFactory();

        Assert.Throws<KeyNotFoundException>(() => factory.Get("NonExistent"));
    }

    [Fact]
    public void Factory_Validate_ShouldReturnResult()
    {
        var factory = new DynamicValidatorFactory();
        var ruleSet = new DynamicRuleSet
        {
            Name = "UserRules",
            Rules = new List<DynamicRule>
            {
                new() { PropertyName = "Name", RuleType = "NotNull" }
            }
        };

        factory.Register("UserRules", ruleSet);

        var result = factory.Validate("UserRules", new UserDto { Name = null });
        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Factory_RegisterFromJson_ShouldWork()
    {
        var factory = new DynamicValidatorFactory();
        var json = """
        {
            "Name": "UserRules",
            "Rules": [
                { "PropertyName": "Name", "RuleType": "NotNull" }
            ]
        }
        """;

        factory.RegisterFromJson("UserRules", json);

        var result = factory.Validate("UserRules", new UserDto { Name = "Alice" });
        Assert.True(result.IsValid);
    }

    #endregion

    #region Culture Independence (BUG-C4)

    // Under a comma-decimal culture the old code turned 0.5 into the text "0,5", which the invariant
    // parser read as 5. Every test here runs the validation under tr-TR and de-DE.

    private sealed class PricedItem
    {
        public decimal Price { get; set; }
    }

    private static DynamicValidator RangeValidator(string min, string max) => DynamicValidator.FromJson($$"""
        {
            "Name": "Prices",
            "Rules": [
                { "PropertyName": "Price", "RuleType": "Range", "Parameters": { "Min": {{min}}, "Max": {{max}} } }
            ]
        }
        """);

    private static GuardResult ValidateUnderCulture(string cultureName, DynamicValidator validator, PricedItem item)
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(cultureName);
        try
        {
            return validator.Validate(item);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Validate_Range_ShouldFail_WhenFractionalValueIsBelowMinUnderCommaDecimalCulture(string cultureName)
    {
        var result = ValidateUnderCulture(cultureName, RangeValidator("1", "1000"), new PricedItem { Price = 0.5m });

        Assert.True(result.IsInvalid);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Validate_Range_ShouldPass_WhenFractionalValueIsInRangeUnderCommaDecimalCulture(string cultureName)
    {
        var result = ValidateUnderCulture(cultureName, RangeValidator("1", "1000"), new PricedItem { Price = 150.75m });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Validate_Range_ShouldPass_WhenValueIsInsideFractionalBoundsUnderCommaDecimalCulture(string cultureName)
    {
        var result = ValidateUnderCulture(cultureName, RangeValidator("0.5", "2.5"), new PricedItem { Price = 2m });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Validate_Range_ShouldFail_WhenValueIsAboveFractionalMaxUnderCommaDecimalCulture(string cultureName)
    {
        var result = ValidateUnderCulture(cultureName, RangeValidator("0.5", "2.5"), new PricedItem { Price = 10m });

        Assert.True(result.IsInvalid);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Validate_GreaterThan_ShouldFail_WhenFractionalValueIsBelowThresholdUnderCommaDecimalCulture(string cultureName)
    {
        var validator = DynamicValidator.FromJson("""
            {
                "Name": "Prices",
                "Rules": [ { "PropertyName": "Price", "RuleType": "GreaterThan", "Parameters": { "Value": 0.5 } } ]
            }
            """);

        var result = ValidateUnderCulture(cultureName, validator, new PricedItem { Price = 0.25m });

        Assert.True(result.IsInvalid);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Validate_Range_ShouldFail_WhenClrDecimalParameterIsAboveValueUnderCommaDecimalCulture(string cultureName)
    {
        var validator = new DynamicValidator(new DynamicRuleSet
        {
            Name = "Prices",
            Rules = new List<DynamicRule>
            {
                new() { PropertyName = "Price", RuleType = "Range", Parameters = new() { ["Min"] = 0.5m } }
            }
        });

        var result = ValidateUnderCulture(cultureName, validator, new PricedItem { Price = 0.25m });

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Validate_MinLength_ShouldAcceptWholeNumberWrittenAsDecimal_WhenParameterIsJsonDouble()
    {
        var validator = DynamicValidator.FromJson("""
            {
                "Name": "Names",
                "Rules": [ { "PropertyName": "Name", "RuleType": "MinLength", "Parameters": { "Min": 3.0 } } ]
            }
            """);

        Assert.True(validator.Validate(new UserDto { Name = "Al" }).IsInvalid);
        Assert.True(validator.Validate(new UserDto { Name = "Alice" }).IsValid);
    }

    #endregion
}
