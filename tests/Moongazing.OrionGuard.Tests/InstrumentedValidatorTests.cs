using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.OpenTelemetry;

namespace Moongazing.OrionGuard.Tests;

public class InstrumentedValidatorTests
{
    private sealed record Order(string Tenant);

    private sealed record Invoice(int Number);

    /// <summary>Fails when the context names a blocked tenant; records which overload ran.</summary>
    private sealed class TenantValidator : IValidator<Order>
    {
        public ValidationContext? ReceivedContext { get; private set; }

        public GuardResult Validate(Order value) => GuardResult.Success();

        public GuardResult Validate(Order value, ValidationContext context)
        {
            ReceivedContext = context;
            return context.TryGet<string>("blockedTenant", out var blocked) && blocked == value.Tenant
                ? GuardResult.Failure("Tenant", "Tenant is blocked.")
                : GuardResult.Success();
        }

        public Task<GuardResult> ValidateAsync(Order value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));

        public Task<GuardResult> ValidateAsync(Order value, ValidationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value, context));
    }

    private sealed class InvoiceValidator : IValidator<Invoice>
    {
        public GuardResult Validate(Invoice value) => GuardResult.Success();

        public Task<GuardResult> ValidateAsync(Invoice value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));
    }

    private sealed class OpenGenericValidator<T> : IValidator<T>
    {
        public GuardResult Validate(T value) => GuardResult.Success();

        public Task<GuardResult> ValidateAsync(T value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));
    }

    #region ValidationContext forwarding (SOLID-3)

    [Fact]
    public void Validate_ShouldForwardContextToInner_WhenContextOverloadIsCalled()
    {
        var inner = new TenantValidator();
        IValidator<Order> instrumented = new InstrumentedValidator<Order>(inner);
        var context = ValidationContext.Empty.With("blockedTenant", "acme");

        var result = instrumented.Validate(new Order("acme"), context);

        Assert.True(result.IsInvalid);
        Assert.Same(context, inner.ReceivedContext);
    }

    [Fact]
    public async Task ValidateAsync_ShouldForwardContextToInner_WhenContextOverloadIsCalled()
    {
        var inner = new TenantValidator();
        IValidator<Order> instrumented = new InstrumentedValidator<Order>(inner);
        var context = ValidationContext.Empty.With("blockedTenant", "acme");

        var result = await instrumented.ValidateAsync(new Order("acme"), context);

        Assert.True(result.IsInvalid);
        Assert.Same(context, inner.ReceivedContext);
    }

    [Fact]
    public void Validate_ShouldCallContextlessOverload_WhenNoContextIsGiven()
    {
        var inner = new TenantValidator();
        IValidator<Order> instrumented = new InstrumentedValidator<Order>(inner);

        Assert.True(instrumented.Validate(new Order("acme")).IsValid);
        Assert.Null(inner.ReceivedContext);
    }

    #endregion

    #region AddOrionGuardOpenTelemetry (BUG-I9)

    [Fact]
    public void AddOrionGuardOpenTelemetry_ShouldNotBreakServiceProvider_WhenOpenGenericValidatorIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddTransient(typeof(IValidator<>), typeof(OpenGenericValidator<>));
        services.AddTransient<IValidator<Invoice>, InvoiceValidator>();

        services.AddOrionGuardOpenTelemetry();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.IsType<OpenGenericValidator<Order>>(provider.GetRequiredService<IValidator<Order>>());
        Assert.IsType<InstrumentedValidator<Invoice>>(provider.GetRequiredService<IValidator<Invoice>>());
    }

    [Fact]
    public void AddOrionGuardOpenTelemetry_ShouldLeaveKeyedValidatorUntouched_WhenKeyedValidatorIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddKeyedTransient<IValidator<Invoice>, InvoiceValidator>("billing");

        services.AddOrionGuardOpenTelemetry();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<InvoiceValidator>(provider.GetRequiredKeyedService<IValidator<Invoice>>("billing"));
    }

    [Fact]
    public void AddOrionGuardOpenTelemetry_ShouldWrapInstanceAndFactoryRegistrations_WhenValidatorsAreClosed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IValidator<Invoice>>(new InvoiceValidator());
        services.AddTransient<IValidator<Order>>(_ => new TenantValidator());

        services.AddOrionGuardOpenTelemetry();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<InstrumentedValidator<Invoice>>(provider.GetRequiredService<IValidator<Invoice>>());
        Assert.IsType<InstrumentedValidator<Order>>(provider.GetRequiredService<IValidator<Order>>());
    }

    #endregion
}
