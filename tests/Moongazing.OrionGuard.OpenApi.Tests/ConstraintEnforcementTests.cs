using System.Reflection;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.OpenApi.Tests;

/// <summary>
/// End-to-end behavioural tests: for each supported constraint kind the generator emits a validator,
/// the validator is compiled and executed, and the test asserts that valid input passes and each
/// individual violation fails with the expected error code. The DTO and document are shared so the
/// suite mirrors one realistic schema rather than a synthetic per-test shape.
/// </summary>
public class ConstraintEnforcementTests
{
    // A consumer DTO whose members cover every supported constraint category, plus the annotated
    // validator. The validator infers T = Customer from its IValidator<Customer> base interface, which
    // the generator completes.
    private const string ConsumerSource = """
        using System;
        using System.Collections.Generic;
        using Moongazing.OrionGuard.DependencyInjection;

        namespace Sample
        {
            public sealed class Customer
            {
                public string Email { get; set; } = "";
                public string Name { get; set; } = "";
                public string Code { get; set; } = "";
                public string Id { get; set; } = "";
                public string Website { get; set; } = "";
                public string CreatedAt { get; set; } = "";
                public int Age { get; set; }
                public decimal Balance { get; set; }
                public int Rating { get; set; }
                public string Tier { get; set; } = "";
                public int Priority { get; set; }
                public List<string> Tags { get; set; } = new();
                public string? Nickname { get; set; }
            }

            [Moongazing.OrionGuard.OpenApi.OpenApiValidator("customer.json", "#/components/schemas/Customer")]
            public partial class CustomerValidator : IValidator<Customer> { }
        }
        """;

    private const string CustomerDocument = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Sample", "version": "1.0.0" },
          "components": {
            "schemas": {
              "Customer": {
                "type": "object",
                "required": ["email", "name"],
                "properties": {
                  "email":     { "type": "string", "format": "email" },
                  "name":      { "type": "string", "minLength": 2, "maxLength": 10 },
                  "code":      { "type": "string", "pattern": "^[A-Z]{3}$" },
                  "id":        { "type": "string", "format": "uuid" },
                  "website":   { "type": "string", "format": "uri" },
                  "createdAt": { "type": "string", "format": "date-time" },
                  "age":       { "type": "integer", "minimum": 18, "maximum": 120 },
                  "balance":   { "type": "number", "exclusiveMinimum": true, "minimum": 0 },
                  "rating":    { "type": "integer", "minimum": 1, "maximum": 5 },
                  "tier":      { "type": "string", "enum": ["bronze", "silver", "gold"] },
                  "priority":  { "type": "integer", "enum": [1, 2, 3] },
                  "tags":      { "type": "array", "minItems": 1, "maxItems": 3 },
                  "nickname":  { "type": "string", "nullable": true, "minLength": 3 }
                }
              }
            }
          }
        }
        """;

    private static readonly Assembly CompiledAssembly =
        GeneratorTestHarness.Compile(ConsumerSource, "customer.json", CustomerDocument);

    private static object NewValidCustomer()
    {
        var customerType = CompiledAssembly.GetType("Sample.Customer")!;
        dynamic customer = Activator.CreateInstance(customerType)!;
        customer.Email = "user@example.com";
        customer.Name = "Ada";
        customer.Code = "ABC";
        customer.Id = "12345678-1234-1234-1234-123456789abc";
        customer.Website = "https://example.com";
        customer.CreatedAt = "2026-06-23T10:30:00Z";
        customer.Age = 30;
        customer.Balance = 100m;
        customer.Rating = 4;
        customer.Tier = "gold";
        customer.Priority = 2;
        customer.Tags = new List<string> { "vip" };
        customer.Nickname = null;
        return customer;
    }

    private static GuardResult Validate(object customer) =>
        GeneratorTestHarness.Validate(CompiledAssembly, "Sample.CustomerValidator", customer);

    private static bool HasCode(GuardResult result, string code) =>
        result.Errors.Any(e => e.ErrorCode == code);

    [Fact]
    public void ValidCustomer_PassesAllConstraints()
    {
        var result = Validate(NewValidCustomer());

        Assert.True(
            result.IsValid,
            "Expected a fully valid customer to pass, but got: "
            + string.Join("; ", result.Errors.Select(e => $"{e.ParameterName}:{e.ErrorCode}:{e.Message}")));
    }

    [Fact]
    public void Required_MissingReferenceProperty_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Email = null;

        var result = Validate((object)customer);

        Assert.True(HasCode(result, "REQUIRED"));
        Assert.Contains(result.Errors, e => e.ParameterName == "email");
    }

    [Fact]
    public void Format_Email_Invalid_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Email = "not-an-email";

        var result = Validate((object)customer);

        Assert.True(HasCode(result, "FORMAT"));
    }

    [Fact]
    public void StringMinLength_TooShort_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Name = "A";

        var result = Validate((object)customer);

        Assert.True(HasCode(result, "MIN_LENGTH"));
    }

    [Fact]
    public void StringMaxLength_TooLong_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Name = "ThisNameIsWayTooLong";

        var result = Validate((object)customer);

        Assert.True(HasCode(result, "MAX_LENGTH"));
    }

    [Fact]
    public void Pattern_Mismatch_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Code = "abc"; // lowercase fails ^[A-Z]{3}$

        var result = Validate((object)customer);

        Assert.True(HasCode(result, "PATTERN"));
    }

    [Fact]
    public void Format_Uuid_Invalid_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Id = "not-a-uuid";

        var result = Validate((object)customer);

        Assert.True(HasCode(result, "FORMAT"));
        Assert.Contains(result.Errors, e => e.ParameterName == "id");
    }

    [Fact]
    public void Format_Uri_Invalid_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Website = "  not a uri  ";

        var result = Validate((object)customer);

        Assert.Contains(result.Errors, e => e.ParameterName == "website" && e.ErrorCode == "FORMAT");
    }

    [Fact]
    public void Format_DateTime_Invalid_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.CreatedAt = "23/06/2026";

        var result = Validate((object)customer);

        Assert.Contains(result.Errors, e => e.ParameterName == "createdAt" && e.ErrorCode == "FORMAT");
    }

    [Fact]
    public void NumericMinimum_BelowBound_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Age = 17;

        var result = Validate((object)customer);

        Assert.Contains(result.Errors, e => e.ParameterName == "age" && e.ErrorCode == "MINIMUM");
    }

    [Fact]
    public void NumericMaximum_AboveBound_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Age = 121;

        var result = Validate((object)customer);

        Assert.Contains(result.Errors, e => e.ParameterName == "age" && e.ErrorCode == "MAXIMUM");
    }

    [Fact]
    public void ExclusiveMinimum_AtBound_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Balance = 0m; // exclusiveMinimum 0 means 0 is invalid

        var result = Validate((object)customer);

        Assert.Contains(result.Errors, e => e.ParameterName == "balance" && e.ErrorCode == "MINIMUM");
    }

    [Fact]
    public void ExclusiveMinimum_AboveBound_Passes()
    {
        dynamic customer = NewValidCustomer();
        customer.Balance = 0.01m;

        var result = Validate((object)customer);

        Assert.DoesNotContain(result.Errors, e => e.ParameterName == "balance");
    }

    [Fact]
    public void Enum_String_NotAllowed_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Tier = "platinum";

        var result = Validate((object)customer);

        Assert.Contains(result.Errors, e => e.ParameterName == "tier" && e.ErrorCode == "ENUM");
    }

    [Fact]
    public void Enum_Numeric_NotAllowed_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Priority = 9;

        var result = Validate((object)customer);

        Assert.Contains(result.Errors, e => e.ParameterName == "priority" && e.ErrorCode == "ENUM");
    }

    [Fact]
    public void ArrayMinItems_TooFew_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Tags = new List<string>();

        var result = Validate((object)customer);

        Assert.Contains(result.Errors, e => e.ParameterName == "tags" && e.ErrorCode == "MIN_ITEMS");
    }

    [Fact]
    public void ArrayMaxItems_TooMany_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Tags = new List<string> { "a", "b", "c", "d" };

        var result = Validate((object)customer);

        Assert.Contains(result.Errors, e => e.ParameterName == "tags" && e.ErrorCode == "MAX_ITEMS");
    }

    [Fact]
    public void Nullable_NullValue_SkipsValueConstraints()
    {
        // nickname is nullable with minLength 3; a null value must not trip minLength.
        dynamic customer = NewValidCustomer();
        customer.Nickname = null;

        var result = Validate((object)customer);

        Assert.DoesNotContain(result.Errors, e => e.ParameterName == "nickname");
    }

    [Fact]
    public void Nullable_PresentButInvalid_Fails()
    {
        dynamic customer = NewValidCustomer();
        customer.Nickname = "ab"; // present, below minLength 3

        var result = Validate((object)customer);

        Assert.Contains(result.Errors, e => e.ParameterName == "nickname" && e.ErrorCode == "MIN_LENGTH");
    }

    [Fact]
    public void MultipleViolations_AllAccumulate()
    {
        dynamic customer = NewValidCustomer();
        customer.Name = "A";          // MIN_LENGTH
        customer.Age = 5;             // MINIMUM
        customer.Tier = "platinum";   // ENUM

        var result = Validate((object)customer);

        Assert.True(HasCode(result, "MIN_LENGTH"));
        Assert.True(HasCode(result, "MINIMUM"));
        Assert.True(HasCode(result, "ENUM"));
    }
}
