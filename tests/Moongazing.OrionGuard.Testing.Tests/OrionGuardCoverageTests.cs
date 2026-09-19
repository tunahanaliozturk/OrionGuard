using Moongazing.OrionGuard.Testing.Validators;

namespace Moongazing.OrionGuard.Testing.Tests;

public class OrionGuardCoverageTests
{
    [Fact]
    public void AssertEveryPropertyIsValidated_Throws_AndNamesTheUnvalidatedProperty()
    {
        var coverage = OrionGuardCoverage.For<CreateUserValidator, CreateUserRequest>();

        var exception = Assert.Throws<ValidatorAssertionException>(
            () => coverage.AssertEveryPropertyIsValidated());

        Assert.Contains(nameof(CreateUserRequest.Notes), exception.Message, StringComparison.Ordinal);
        Assert.Equal([nameof(CreateUserRequest.Notes)], coverage.UnvalidatedProperties);
    }

    [Fact]
    public void AssertEveryPropertyIsValidated_Passes_WhenTheGapIsExcepted()
        => OrionGuardCoverage.For<CreateUserValidator, CreateUserRequest>()
            .AssertEveryPropertyIsValidated(except: [nameof(CreateUserRequest.Notes)]);

    [Fact]
    public void AssertEveryPropertyIsValidated_Throws_WhenExceptNamesSomethingThatIsNotAProperty()
    {
        var coverage = OrionGuardCoverage.For<CreateUserValidator, CreateUserRequest>();

        var exception = Assert.Throws<ValidatorAssertionException>(
            () => coverage.AssertEveryPropertyIsValidated(except: ["Remarks"]));

        Assert.Contains("Remarks", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void For_Throws_NamingTheValidator_WhenItsRulesCannotBeEnumerated()
    {
        var exception = Assert.Throws<ValidatorAssertionException>(
            () => OrionGuardCoverage.For<NeverFailingValidator, CreateUserRequest>());

        Assert.Contains(nameof(NeverFailingValidator), exception.Message, StringComparison.Ordinal);
        Assert.Contains("could not be enumerated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ValidationAttribute_CountsAsARule()
        => OrionGuardCoverage.For<TokenValidator, TokenRequest>().AssertEveryPropertyIsValidated();

    [Fact]
    public void An_AsyncOnlyRule_CountsAsCoverage()
    {
        // Probing through Validate would see nothing here and throw "could not be enumerated".
        var coverage = OrionGuardCoverage.For<AsyncOnlyValidator, CreateUserRequest>();

        Assert.DoesNotContain(nameof(CreateUserRequest.Email), coverage.UnvalidatedProperties);
        coverage.AssertEveryPropertyIsValidated(except:
        [
            nameof(CreateUserRequest.Age),
            nameof(CreateUserRequest.Name),
            nameof(CreateUserRequest.Notes),
        ]);
    }

    [Fact]
    public void A_MixedValidator_CoversItsSyncAndItsAsyncProperty()
    {
        var coverage = OrionGuardCoverage.For<MixedRulesValidator, CreateUserRequest>();

        Assert.Equal(
            [nameof(CreateUserRequest.Email), nameof(CreateUserRequest.Notes)],
            coverage.UnvalidatedProperties);
    }

    [Fact]
    public void A_RuleOnAConcreteCollectionType_CountsAsCoverage()
        => OrionGuardCoverage.For<TagsValidator, TagsRequest>().AssertEveryPropertyIsValidated();
}
