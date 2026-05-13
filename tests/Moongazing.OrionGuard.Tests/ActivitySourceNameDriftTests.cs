using System.Diagnostics;
using System.Reflection;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.OpenTelemetry.DomainEvents;

namespace Moongazing.OrionGuard.Tests;

public class ActivitySourceNameDriftTests
{
    [Fact]
    public void OutboxDispatcherHostedService_UsesSameActivitySourceName_AsOpenTelemetryInstrumentation()
    {
        // The constant exposed by OrionGuard.OpenTelemetry is the source of truth.
        var expected = OrionGuardDomainEventTelemetry.ActivitySourceName;

        // The string baked into OutboxDispatcherHostedService is held by a private static
        // ActivitySource field. Reflect into it and read its Name.
        var fieldNames = new[] { "OutboxActivitySource", "outboxActivitySource", "ActivitySource", "activitySource" };
        FieldInfo? field = null;
        foreach (var name in fieldNames)
        {
            field = typeof(OutboxDispatcherHostedService).GetField(
                name, BindingFlags.Static | BindingFlags.NonPublic);
            if (field is not null) break;
        }
        Assert.NotNull(field);
        var source = (ActivitySource)field!.GetValue(null)!;

        Assert.Equal(expected, source.Name);
    }
}
