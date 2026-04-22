using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Tests;

public class DomainEventBaseTests
{
    private sealed record OrderPlaced(int OrderNumber) : DomainEventBase;

    [Fact]
    public void DomainEventBase_ShouldAssignNonEmptyEventId_WhenConstructed()
    {
        var evt = new OrderPlaced(42);

        Assert.NotEqual(Guid.Empty, evt.EventId);
    }

    [Fact]
    public void DomainEventBase_ShouldAssignUtcTimestamp_WhenConstructed()
    {
        var before = DateTime.UtcNow;
        var evt = new OrderPlaced(42);
        var after = DateTime.UtcNow;

        Assert.InRange(evt.OccurredOnUtc, before, after);
        Assert.Equal(DateTimeKind.Utc, evt.OccurredOnUtc.Kind);
    }

    [Fact]
    public void DomainEventBase_ShouldAllowTestOverrides_ViaWithExpression()
    {
        var fixedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fixedTimestamp = new DateTime(2026, 4, 19, 10, 0, 0, DateTimeKind.Utc);

        var evt = new OrderPlaced(42) with { EventId = fixedId, OccurredOnUtc = fixedTimestamp };

        Assert.Equal(fixedId, evt.EventId);
        Assert.Equal(fixedTimestamp, evt.OccurredOnUtc);
    }

    [Fact]
    public void DomainEventBase_ShouldImplementIDomainEvent_WhenUpcast()
    {
        IDomainEvent evt = new OrderPlaced(42);

        Assert.NotEqual(Guid.Empty, evt.EventId);
        Assert.NotEqual(default, evt.OccurredOnUtc);
    }
}
