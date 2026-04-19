using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Tests;

public class CachedValidatorTests
{
    #region Test DTOs and Validators

    private sealed class Product
    {
        public string? Name { get; set; }
        public decimal Price { get; set; }
    }

    private sealed class ProductValidator : IValidator<Product>
    {
        public int CallCount { get; private set; }

        public GuardResult Validate(Product value)
        {
            CallCount++;
            if (string.IsNullOrWhiteSpace(value.Name))
                return GuardResult.Failure("Name", "Name is required.");
            return GuardResult.Success();
        }

        public Task<GuardResult> ValidateAsync(Product value, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Validate(value));
        }
    }

    #endregion

    #region First Call Validates, Second Returns Cached

    [Fact]
    public void Validate_ShouldCallInnerOnFirstCall()
    {
        var inner = new ProductValidator();
        var cached = new CachedValidator<Product>(inner, TimeSpan.FromMinutes(5));

        var product = new Product { Name = "Widget", Price = 9.99m };
        var result = cached.Validate(product);

        Assert.True(result.IsValid);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public void Validate_ShouldReturnCachedResult_OnSecondCallWithSameInput()
    {
        var inner = new ProductValidator();
        var cached = new CachedValidator<Product>(inner, TimeSpan.FromMinutes(5));

        var product = new Product { Name = "Widget", Price = 9.99m };

        cached.Validate(product);
        cached.Validate(product);

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public void Validate_ShouldCallInnerAgain_WhenInputChanges()
    {
        var inner = new ProductValidator();
        var cached = new CachedValidator<Product>(inner, TimeSpan.FromMinutes(5));

        cached.Validate(new Product { Name = "Widget", Price = 9.99m });
        cached.Validate(new Product { Name = "Gadget", Price = 19.99m });

        Assert.Equal(2, inner.CallCount);
    }

    #endregion

    #region Cache Expiry (TTL)

    [Fact]
    public async Task Validate_ShouldRevalidate_AfterTtlExpires()
    {
        var inner = new ProductValidator();
        var cached = new CachedValidator<Product>(inner, TimeSpan.FromMilliseconds(50));

        var product = new Product { Name = "Widget", Price = 9.99m };

        cached.Validate(product);
        Assert.Equal(1, inner.CallCount);

        await Task.Delay(100);

        cached.Validate(product);
        Assert.Equal(2, inner.CallCount);
    }

    #endregion

    #region ClearCache

    [Fact]
    public void ClearCache_ShouldForceRevalidation()
    {
        var inner = new ProductValidator();
        var cached = new CachedValidator<Product>(inner, TimeSpan.FromMinutes(5));

        var product = new Product { Name = "Widget", Price = 9.99m };

        cached.Validate(product);
        Assert.Equal(1, inner.CallCount);
        Assert.Equal(1, cached.CacheSize);

        cached.ClearCache();
        Assert.Equal(0, cached.CacheSize);

        cached.Validate(product);
        Assert.Equal(2, inner.CallCount);
    }

    #endregion

    #region WithCaching Extension Method

    [Fact]
    public void WithCaching_ShouldWrapValidatorWithCaching()
    {
        var inner = new ProductValidator();
        var cached = inner.WithCaching(TimeSpan.FromMinutes(10));

        Assert.NotNull(cached);
        Assert.IsType<CachedValidator<Product>>(cached);
    }

    [Fact]
    public void WithCaching_ShouldCacheResults()
    {
        var inner = new ProductValidator();
        var cached = inner.WithCaching(TimeSpan.FromMinutes(10));

        var product = new Product { Name = "Widget", Price = 9.99m };

        cached.Validate(product);
        cached.Validate(product);

        Assert.Equal(1, inner.CallCount);
    }

    #endregion

    #region Async Validation

    [Fact]
    public async Task ValidateAsync_ShouldCacheResults()
    {
        var inner = new ProductValidator();
        var cached = new CachedValidator<Product>(inner, TimeSpan.FromMinutes(5));

        var product = new Product { Name = "Widget", Price = 9.99m };

        await cached.ValidateAsync(product);
        await cached.ValidateAsync(product);

        Assert.Equal(1, inner.CallCount);
    }

    #endregion

    #region Invalid Results Also Cached

    [Fact]
    public void Validate_ShouldCacheInvalidResults()
    {
        var inner = new ProductValidator();
        var cached = new CachedValidator<Product>(inner, TimeSpan.FromMinutes(5));

        var product = new Product { Name = null, Price = 0 };

        var result1 = cached.Validate(product);
        var result2 = cached.Validate(product);

        Assert.True(result1.IsInvalid);
        Assert.True(result2.IsInvalid);
        Assert.Equal(1, inner.CallCount);
    }

    #endregion
}
