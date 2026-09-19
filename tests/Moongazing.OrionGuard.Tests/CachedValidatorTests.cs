using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Tests;

public class CachedValidatorTests
{
    #region Test DTOs and Validators

    // A record, so the cache can prove two inputs are equal. A plain class bypasses the cache (see the
    // Validate_ShouldNotUseCache_When... tests below).
    private sealed record Product
    {
        public string? Name { get; set; }
        public decimal Price { get; set; }
    }

    // Plain classes (no IEquatable<T>) used by the probe that found the key collisions.
    private sealed class Cart
    {
        public List<int> Items { get; set; } = new();
    }

    private sealed class Person
    {
        public string? Name { get; set; }
    }

    private sealed class CartValidator : AbstractValidator<Cart>
    {
        public CartValidator()
        {
            RuleFor(c => c.Items.Count > 0, "Items required", "Items");
        }
    }

    private sealed class PersonValidator : AbstractValidator<Person>
    {
        public PersonValidator()
        {
            RuleFor(p => p.Name, "Name", v => v.NotNull());
        }
    }

    /// <summary>Counts calls and records the context each call received.</summary>
    private sealed class ContextRecordingValidator : IValidator<Product>
    {
        public int CallCount { get; private set; }
        public List<ValidationContext?> ReceivedContexts { get; } = new();

        public GuardResult Validate(Product value)
        {
            CallCount++;
            ReceivedContexts.Add(null);
            return GuardResult.Success();
        }

        public GuardResult Validate(Product value, ValidationContext context)
        {
            CallCount++;
            ReceivedContexts.Add(context);
            return context.TryGet<string>("tenant", out var tenant) && tenant == "blocked"
                ? GuardResult.Failure("Tenant", "Tenant is blocked.")
                : GuardResult.Success();
        }

        public Task<GuardResult> ValidateAsync(Product value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));

        public Task<GuardResult> ValidateAsync(Product value, ValidationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value, context));
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

    #region Cache Key Correctness (BUG-C3)

    [Fact]
    public void Validate_ShouldNotServeCachedValid_WhenCollectionContentDiffers()
    {
        // Both carts used to produce the key "Items=System.Collections.Generic.List`1[System.Int32];".
        var cached = new CartValidator().WithCaching();

        Assert.True(cached.Validate(new Cart { Items = new() { 1 } }).IsValid);

        Assert.True(cached.Validate(new Cart { Items = new() }).IsInvalid);
    }

    [Fact]
    public void Validate_ShouldNotServeCachedValid_WhenNullAndTheStringNullCollide()
    {
        // Name = "null" and Name = null both used to produce the key "Name=null;".
        var cached = new PersonValidator().WithCaching();

        Assert.True(cached.Validate(new Person { Name = "null" }).IsValid);

        Assert.True(cached.Validate(new Person { Name = null }).IsInvalid);
    }

    [Fact]
    public void Validate_ShouldNotServeCachedValid_WhenTypeEqualityIgnoresValidatedState()
    {
        // An entity compared by Id: equal to its cached twin while IsBlocked differs.
        var cached = new AccountValidator().WithCaching();

        Assert.True(cached.Validate(new Account(1) { IsBlocked = false }).IsValid);

        Assert.True(cached.Validate(new Account(1) { IsBlocked = true }).IsInvalid);
    }

    private sealed class Account : IEquatable<Account>
    {
        public Account(int id) => Id = id;

        public int Id { get; }

        public bool IsBlocked { get; init; }

        public bool Equals(Account? other) => other is not null && other.Id == Id;

        public override bool Equals(object? obj) => Equals(obj as Account);

        public override int GetHashCode() => Id;
    }

    private sealed class AccountValidator : AbstractValidator<Account>
    {
        public AccountValidator()
        {
            RuleFor(a => !a.IsBlocked, "Account is blocked.", "IsBlocked");
        }
    }

    [Fact]
    public void Validate_ShouldNotUseCache_WhenTypeHasNoValueEquality()
    {
        var inner = new CountingPersonValidator();
        var cached = inner.WithCaching();
        var person = new Person { Name = "Ada" };

        cached.Validate(person);
        cached.Validate(person);

        Assert.Equal(2, inner.CallCount);
        Assert.Equal(0, cached.CacheSize);
    }

    [Fact]
    public void Validate_ShouldCacheByRecordEquality_WhenTypeIsARecord()
    {
        var inner = new ProductValidator();
        var cached = inner.WithCaching();

        cached.Validate(new Product { Name = "Widget", Price = 1m });
        cached.Validate(new Product { Name = "Widget", Price = 1m });
        cached.Validate(new Product { Name = "Widget", Price = 2m });

        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public void WithCaching_ShouldCacheByKeySelector_WhenKeySelectorIsSupplied()
    {
        var inner = new CountingPersonValidator();
        var cached = inner.WithCaching(p => p.Name ?? string.Empty);

        cached.Validate(new Person { Name = "Ada" });
        cached.Validate(new Person { Name = "Ada" });
        cached.Validate(new Person { Name = "Grace" });

        Assert.Equal(2, inner.CallCount);
        Assert.Equal(2, cached.CacheSize);
    }

    [Fact]
    public void WithCaching_ShouldThrow_WhenKeySelectorIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CountingPersonValidator().WithCaching<Person, string>(null!));
    }

    [Fact]
    public void Validate_ShouldKeepCacheSizeWithinBound_WhenMoreDistinctInputsThanCapacity()
    {
        var cached = new ProductValidator().WithCaching(TimeSpan.FromMinutes(5), maxCacheSize: 2);

        for (var i = 0; i < 5; i++)
            cached.Validate(new Product { Name = "Widget", Price = i });

        Assert.True(cached.CacheSize <= 2, $"CacheSize was {cached.CacheSize}.");
    }

    #endregion

    #region ValidationContext (SOLID-3)

    [Fact]
    public void Validate_ShouldForwardContextToInner_WhenContextIsNotEmpty()
    {
        var inner = new ContextRecordingValidator();
        IValidator<Product> cached = inner.WithCaching(); // as resolved from DI
        var context = ValidationContext.Empty.With("tenant", "blocked");

        var result = cached.Validate(new Product { Name = "Widget" }, context);

        Assert.True(result.IsInvalid);
        Assert.Same(context, Assert.Single(inner.ReceivedContexts));
    }

    [Fact]
    public void Validate_ShouldNotShareCachedResult_WhenContextsDiffer()
    {
        var inner = new ContextRecordingValidator();
        IValidator<Product> cached = inner.WithCaching();
        var product = new Product { Name = "Widget" };

        var allowed = cached.Validate(product, ValidationContext.Empty.With("tenant", "allowed"));
        var blocked = cached.Validate(product, ValidationContext.Empty.With("tenant", "blocked"));

        Assert.True(allowed.IsValid);
        Assert.True(blocked.IsInvalid);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task ValidateAsync_ShouldForwardContextToInner_WhenContextIsNotEmpty()
    {
        var inner = new ContextRecordingValidator();
        IValidator<Product> cached = inner.WithCaching();
        var context = ValidationContext.Empty.With("tenant", "blocked");

        var result = await cached.ValidateAsync(new Product { Name = "Widget" }, context);

        Assert.True(result.IsInvalid);
        Assert.Same(context, Assert.Single(inner.ReceivedContexts));
    }

    [Fact]
    public void Validate_ShouldUseCache_WhenContextIsEmpty()
    {
        var inner = new ContextRecordingValidator();
        IValidator<Product> cached = inner.WithCaching();
        var product = new Product { Name = "Widget" };

        cached.Validate(product, ValidationContext.Empty);
        cached.Validate(product);

        Assert.Equal(1, inner.CallCount);
    }

    private sealed class CountingPersonValidator : IValidator<Person>
    {
        public int CallCount { get; private set; }

        public GuardResult Validate(Person value)
        {
            CallCount++;
            return GuardResult.Success();
        }

        public Task<GuardResult> ValidateAsync(Person value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));
    }

    #endregion
}
