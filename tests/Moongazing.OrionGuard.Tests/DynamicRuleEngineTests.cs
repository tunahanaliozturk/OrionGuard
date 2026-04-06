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
}
