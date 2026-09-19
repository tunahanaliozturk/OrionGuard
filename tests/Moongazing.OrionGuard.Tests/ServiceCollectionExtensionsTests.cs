using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Tests;

public class ServiceCollectionExtensionsTests
{
    private sealed record Shipment(string Address);

    private sealed class ShipmentValidator : IValidator<Shipment>
    {
        public GuardResult Validate(Shipment value) =>
            string.IsNullOrWhiteSpace(value.Address) ? GuardResult.Failure("Address", "Address is required.") : GuardResult.Success();

        public Task<GuardResult> ValidateAsync(Shipment value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));
    }

    [Fact]
    public void AddOrionGuard_ShouldResolveRegistryValidatorThroughFactory_WhenRegisteredInValidatorRegistry()
    {
        using var provider = new ServiceCollection()
            .AddOrionGuard(registry => registry.Register<Shipment, ShipmentValidator>())
            .BuildServiceProvider();

        var validator = provider.GetRequiredService<IValidatorFactory>().GetValidator<Shipment>();

        Assert.IsType<ShipmentValidator>(validator);
        Assert.True(validator!.Validate(new Shipment("")).IsInvalid);
    }

    [Fact]
    public void AddOrionGuard_ShouldRegisterRegistryValidatorAsIValidator_WhenRegisteredInValidatorRegistry()
    {
        using var provider = new ServiceCollection()
            .AddOrionGuard(registry => registry.Register<Shipment, ShipmentValidator>())
            .BuildServiceProvider();

        Assert.IsType<ShipmentValidator>(provider.GetService<IValidator<Shipment>>());
    }

    [Fact]
    public void AddOrionGuard_ShouldReturnNullFromFactory_WhenTypeHasNoValidator()
    {
        using var provider = new ServiceCollection()
            .AddOrionGuard(_ => { })
            .BuildServiceProvider();

        Assert.Null(provider.GetRequiredService<IValidatorFactory>().GetValidator<Shipment>());
    }

    private sealed class CancellableShipmentValidator : AbstractValidator<Shipment>
    {
        public CancellationToken Observed { get; private set; }

        public CancellableShipmentValidator()
        {
            RuleForAsync(
                async (_, _, cancellationToken) =>
                {
                    Observed = cancellationToken;
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return true;
                },
                "Address is taken.",
                "Address");
        }
    }

    [Fact]
    public async Task ValidateAsync_ShouldCancelTheRuleItself_WhenTheTokenIsCancelled()
    {
        var validator = new CancellableShipmentValidator();
        using var cancellation = new CancellationTokenSource();

        var validation = validator.ValidateAsync(new Shipment("Ankara"), cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => validation);
        Assert.Equal(cancellation.Token, validator.Observed);
    }

    private sealed class DecoratedShipmentValidator : AbstractValidator<Shipment>
    {
        public DecoratedShipmentValidator()
        {
            RuleFor(v => !string.IsNullOrWhiteSpace(v.Address), "Address is required.", "Address")
                .WithSeverity(Severity.Warning)
                .WithErrorCode("ADDRESS_MISSING");
            RuleFor(v => v.Address.Length < 100, "Address is too long.", "Address");
        }
    }

    [Fact]
    public void Validate_ShouldApplyModifiersToTheirOwnRuleOnly_WhenOnlyOneRuleWasDecorated()
    {
        var result = new DecoratedShipmentValidator().Validate(new Shipment(""));

        var warning = Assert.Single(result.Warnings);
        Assert.Equal("ADDRESS_MISSING", warning.ErrorCode);
        Assert.Equal("Address is required.", warning.Message);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ShouldLeaveTheRuleErrorUntouched_WhenNoModifierWasChained()
    {
        var result = new DecoratedShipmentValidator().Validate(new Shipment(new string('x', 200)));

        var error = Assert.Single(result.Errors);
        Assert.Equal("Address is too long.", error.Message);
        Assert.Null(error.ErrorCode);
        Assert.Equal(Severity.Error, error.Severity);
    }
}
