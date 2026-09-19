using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

/// <summary>
/// BUG-C2: the compiled-accessor caches behind <see cref="ObjectValidator{T}"/> and <see cref="Delta{T}"/>
/// were keyed by the last member name (and "Unknown" for anything else) or by Expression.ToString(), so
/// distinct selectors shared one accessor. Each test uses its own model types: the caches are static, and
/// a type shared with another test could be served an accessor that test compiled.
/// </summary>
public class AccessorCacheTests
{
    private sealed class Customer
    {
        public string Name { get; set; } = "";
    }

    private sealed class NestedNameOrder
    {
        public string Name { get; set; } = "";
        public Customer Customer { get; set; } = new();
    }

    private sealed class CountsOrder
    {
        public List<int> Items { get; set; } = new();
        public List<string> Tags { get; set; } = new();
    }

    private sealed class MethodCallOrder
    {
        public string First { get; set; } = "";
        public string Last { get; set; } = "";
    }

    private sealed class Settings
    {
        public Dictionary<string, int> Values { get; set; } = new();
    }

    [Fact]
    public void Property_ShouldReadOwnMember_WhenNestedMemberWithSameNameWasValidatedFirst()
    {
        var order = new NestedNameOrder { Name = "", Customer = new Customer { Name = "Alice" } };

        Validate.For(order).Property(o => o.Customer.Name, g => g.NotEmpty()).ToResult();
        var result = Validate.For(order).Property(o => o.Name, g => g.NotEmpty()).ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Property_ShouldReadOwnPath_WhenAnotherPathEndsInTheSameMember()
    {
        var order = new CountsOrder { Items = new() { 1 }, Tags = new() };

        Validate.For(order).Property(o => o.Items.Count, g => g.GreaterThan(0)).ToResult();
        var result = Validate.For(order).Property(o => o.Tags.Count, g => g.GreaterThan(0)).ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Property_ShouldEvaluateEachSelector_WhenSelectorsAreNotMemberAccesses()
    {
        // Both selectors used to be cached under the key "Unknown".
        var order = new MethodCallOrder { First = "Ada", Last = "" };

        Validate.For(order).Property(o => o.First.Trim(), g => g.NotEmpty()).ToResult();
        var result = Validate.For(order).Property(o => o.Last.Trim(), g => g.NotEmpty()).ToResult();

        Assert.True(result.IsInvalid);
    }

    [Fact]
    public void Property_ShouldReuseAccessor_WhenTheSameMemberIsValidatedAgain()
    {
        var valid = Validate.For(new Customer { Name = "Ada" }).Property(c => c.Name, g => g.NotEmpty()).ToResult();
        var invalid = Validate.For(new Customer { Name = "" }).Property(c => c.Name, g => g.NotEmpty()).ToResult();

        Assert.True(valid.IsValid);
        Assert.True(invalid.IsInvalid);
    }

    [Fact]
    public void HasChanged_ShouldUseCapturedValue_WhenSameSelectorSiteCapturesDifferentValues()
    {
        // One lambda site, two captured keys: Expression.ToString() prints both the same way.
        var original = new Settings { Values = new() { ["a"] = 1, ["b"] = 1 } };
        var updated = new Settings { Values = new() { ["a"] = 1, ["b"] = 2 } };
        var delta = new Delta<Settings>(original, updated);

        Assert.False(ValueChanged(delta, "a"));
        Assert.True(ValueChanged(delta, "b"));
    }

    [Fact]
    public void ForChanged_ShouldValidateUpdatedValue_WhenSelectorIsAMemberChain()
    {
        var original = new NestedNameOrder { Customer = new Customer { Name = "Alice" } };
        var updated = new NestedNameOrder { Customer = new Customer { Name = "Bob" } };

        var result = Validate.Delta(original, updated)
            .ForChanged(o => o.Customer.Name, g => g.Must(name => name == "Bob", "Expected the updated name."))
            .ToResult();

        Assert.True(result.IsValid);
    }

    private static bool ValueChanged(Delta<Settings> delta, string key) =>
        delta.HasChanged(s => s.Values[key]);
}
