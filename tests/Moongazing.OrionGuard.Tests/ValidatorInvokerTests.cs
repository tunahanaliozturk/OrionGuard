using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Tests;

public sealed class ValidatorInvokerTests
{
    private sealed record Order(string Code, int Quantity);

    private sealed class Unvalidated;

    private sealed class CodeRequiredValidator : IValidator<Order>
    {
        public GuardResult Validate(Order value) =>
            string.IsNullOrEmpty(value.Code)
                ? GuardResult.FailureWithStatus(409, "Code", "Code is required.")
                : GuardResult.Success();

        public Task<GuardResult> ValidateAsync(Order value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));
    }

    private sealed class PositiveQuantityValidator : AbstractValidator<Order>
    {
        public PositiveQuantityValidator()
        {
            RuleFor(o => o.Quantity > 0, "Quantity must be positive.", "Quantity");
        }
    }

    private sealed class AsyncOnlyValidator : AbstractValidator<Order>
    {
        public AsyncOnlyValidator()
        {
            RuleForAsync(async o =>
            {
                await Task.Yield();
                return o.Code != "taken";
            }, "Code is already taken.", "Code");
        }
    }

    private sealed class TenantValidator : AbstractValidator<Order>
    {
        public TenantValidator()
        {
            RuleFor((o, context) => context.TryGet<string>("tenant", out var tenant) && tenant == "acme",
                "Order belongs to another tenant.", "Tenant");
        }
    }

    private sealed class ScopedDependency
    {
        public bool Allows(Order order) => order.Code != "blocked";
    }

    private sealed class ScopedDependencyValidator : IValidator<Order>
    {
        private readonly ScopedDependency dependency;

        public ScopedDependencyValidator(ScopedDependency dependency)
        {
            this.dependency = dependency;
        }

        public GuardResult Validate(Order value) =>
            dependency.Allows(value) ? GuardResult.Success() : GuardResult.Failure("Code", "Code is blocked.");

        public Task<GuardResult> ValidateAsync(Order value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));
    }

    private sealed class FirstLookupValidator : AbstractValidator<Order>
    {
        public FirstLookupValidator(SingleOperationLookup lookup)
        {
            RuleForAsync(o => lookup.IsFreeAsync(o.Code), "Code is taken (first check).", "Code");
        }
    }

    private sealed class SecondLookupValidator : AbstractValidator<Order>
    {
        public SecondLookupValidator(SingleOperationLookup lookup)
        {
            RuleForAsync(o => lookup.IsFreeAsync(o.Code), "Code is taken (second check).", "Code");
        }
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task ValidateAsync_returns_null_when_no_validator_is_registered()
    {
        using var provider = BuildProvider(services => services.AddValidator<Order, CodeRequiredValidator>());

        var result = await ValidatorInvoker.ValidateAsync(provider, new Unvalidated());

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_returns_the_single_validator_result_unchanged()
    {
        using var provider = BuildProvider(services => services.AddValidator<Order, CodeRequiredValidator>());

        var result = await ValidatorInvoker.ValidateAsync(provider, new Order("", 1));

        Assert.NotNull(result);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Code", error.ParameterName);
        Assert.Equal(409, result.SuggestedHttpStatusCode);
    }

    [Fact]
    public async Task ValidateAsync_combines_every_registered_validator_in_registration_order()
    {
        using var provider = BuildProvider(services => services
            .AddValidator<Order, CodeRequiredValidator>()
            .AddValidator<Order, PositiveQuantityValidator>());

        var result = await ValidatorInvoker.ValidateAsync(provider, new Order("", 0));

        Assert.NotNull(result);
        Assert.Equal(new[] { "Code", "Quantity" }, result.Errors.Select(e => e.ParameterName));
    }

    [Fact]
    public async Task ValidateAsync_returns_valid_result_when_every_validator_passes()
    {
        using var provider = BuildProvider(services => services
            .AddValidator<Order, CodeRequiredValidator>()
            .AddValidator<Order, PositiveQuantityValidator>());

        var result = await ValidatorInvoker.ValidateAsync(provider, new Order("A-1", 2));

        Assert.NotNull(result);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_runs_async_only_rules()
    {
        using var provider = BuildProvider(services => services.AddValidator<Order, AsyncOnlyValidator>());

        var result = await ValidatorInvoker.ValidateAsync(provider, new Order("taken", 1));

        Assert.NotNull(result);
        Assert.Equal("Code is already taken.", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public async Task ValidateAsync_passes_the_validation_context_to_the_validator()
    {
        using var provider = BuildProvider(services => services.AddValidator<Order, TenantValidator>());
        var order = new Order("A-1", 1);

        var withTenant = await ValidatorInvoker.ValidateAsync(provider, order, ValidationContext.Empty.With("tenant", "acme"));
        var withoutContext = await ValidatorInvoker.ValidateAsync(provider, order);

        Assert.True(withTenant!.IsValid);
        Assert.True(withoutContext!.IsInvalid);
    }

    [Fact]
    public async Task ValidateAsync_resolves_scoped_validators_from_the_provider_it_is_given()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddScoped<ScopedDependency>();
            services.AddScoped<IValidator<Order>, ScopedDependencyValidator>();
        });
        using var scope = provider.CreateScope();

        var result = await ValidatorInvoker.ValidateAsync(scope.ServiceProvider, new Order("blocked", 1));

        Assert.NotNull(result);
        Assert.Equal("Code is blocked.", Assert.Single(result.Errors).Message);
        // The root provider refuses scoped services, which proves resolution used the provider passed in.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ValidatorInvoker.ValidateAsync(provider, new Order("blocked", 1)));
    }

    [Fact]
    public async Task ValidateAsync_runs_validators_one_at_a_time_so_they_can_share_a_scoped_dependency()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddScoped<SingleOperationLookup>();
            services.AddScoped<IValidator<Order>, FirstLookupValidator>();
            services.AddScoped<IValidator<Order>, SecondLookupValidator>();
        });
        using var scope = provider.CreateScope();

        var result = await ValidatorInvoker.ValidateAsync(scope.ServiceProvider, new Order("free", 1));

        Assert.NotNull(result);
        Assert.Empty(result.AllIssues);
    }

    [Fact]
    public async Task ValidateAsync_throws_when_arguments_are_null()
    {
        using var provider = BuildProvider(_ => { });

        await Assert.ThrowsAsync<ArgumentNullException>(() => ValidatorInvoker.ValidateAsync(null!, new Order("A", 1)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ValidatorInvoker.ValidateAsync(provider, null!));
    }
}
