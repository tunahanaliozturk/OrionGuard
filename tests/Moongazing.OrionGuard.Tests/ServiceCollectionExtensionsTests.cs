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
}
