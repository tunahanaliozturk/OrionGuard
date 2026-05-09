# OrionGuard.EntityFrameworkCore

EF Core integration for OrionGuard's domain-event dispatcher.

```bash
dotnet add package OrionGuard.EntityFrameworkCore
```

```csharp
services.AddOrionGuardDomainEvents();
services.AddOrionGuardDomainEventHandlers(typeof(Program).Assembly);
services.AddOrionGuardEfCore<AppDbContext>(o => o.UseOutbox());
```

See the main [OrionGuard README](https://github.com/tunahanaliozturk/OrionGuard) for full documentation.
