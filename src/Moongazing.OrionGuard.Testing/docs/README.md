# OrionGuard.Testing

Test helpers for OrionGuard. No test-framework dependency — works with xUnit, NUnit, MSTest, etc.

```bash
dotnet add package OrionGuard.Testing
```

```csharp
var events = DomainEventCapture.From(aggregate);
events.Should().HaveRaised<OrderShipped>(e => e.OrderId == expectedId);
```
