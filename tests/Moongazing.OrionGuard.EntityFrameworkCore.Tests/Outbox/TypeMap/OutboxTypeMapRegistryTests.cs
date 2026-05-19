using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.TypeMap;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.TypeMap;

public class OutboxTypeMapRegistryTests
{
    private sealed record UserRegistered : DomainEventBase;
    private sealed record OrderPlaced : DomainEventBase;

    [Fact]
    public void Map_ShouldStoreRoundTrip()
    {
        var r = new OutboxTypeMapRegistry().Map<UserRegistered>("user.registered");

        Assert.True(r.TryResolve("user.registered", out var t));
        Assert.Equal(typeof(UserRegistered), t);

        Assert.True(r.TryGetLogicalName(typeof(UserRegistered), out var name));
        Assert.Equal("user.registered", name);
    }

    [Fact]
    public void TryResolve_ShouldReturnFalse_WhenLogicalNameUnknown()
    {
        var r = new OutboxTypeMapRegistry();
        Assert.False(r.TryResolve("nope", out var t));
        Assert.Null(t);
    }

    [Fact]
    public void Map_ShouldThrow_WhenSameNameMapsToDifferentType()
    {
        var r = new OutboxTypeMapRegistry().Map<UserRegistered>("evt");
        Assert.Throws<InvalidOperationException>(() => r.Map<OrderPlaced>("evt"));
    }

    [Fact]
    public void Map_ShouldThrow_WhenSameTypeMapsToDifferentName()
    {
        var r = new OutboxTypeMapRegistry().Map<UserRegistered>("a");
        Assert.Throws<InvalidOperationException>(() => r.Map<UserRegistered>("b"));
    }

    [Fact]
    public void Map_ShouldBeIdempotent_WhenSameTypeAndSameName()
    {
        var r = new OutboxTypeMapRegistry().Map<UserRegistered>("u");
        r.Map<UserRegistered>("u"); // no throw
    }

    [Fact]
    public void OutboxTypeMapOptions_AllowAssemblyQualifiedNameFallback_ShouldDefaultTrue()
    {
        Assert.True(new OutboxTypeMapOptions().AllowAssemblyQualifiedNameFallback);
    }
}
