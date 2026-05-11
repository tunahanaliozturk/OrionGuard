using Moongazing.OrionGuard.Demo;

Console.WriteLine("OrionGuard v6.3.0 - Feature Demo");
Console.WriteLine(new string('=', 60));

CoreApiDemo.Run();
ObjectValidationDemo.Run();
SecurityGuardsDemo.Run();
LocalizationDemo.Run();
ProfilesDemo.Run();
LegacyApiDemo.Run();
StronglyTypedIdsDemo.Run();
await DddPrimitivesDemo.RunAsync();
await DomainEventsDemo.RunAsync();

Console.WriteLine();
Console.WriteLine(new string('=', 60));
Console.WriteLine("Summary");
Console.WriteLine(new string('=', 60));
Console.WriteLine("Demos covered: core fluent API, object validation, security guards,");
Console.WriteLine("format guards, localization, profiles, legacy API, strongly-typed IDs,");
Console.WriteLine("DDD primitives (Value Objects, Entity, AggregateRoot, business rules),");
Console.WriteLine("and domain events with the EF Core SaveChanges interceptor (Inline mode).");
Console.WriteLine();
Console.WriteLine("v6.3.0 highlights: IDomainEventDispatcher + 3 dispatch modes,");
Console.WriteLine("MediatR bridge, OrionGuard.EntityFrameworkCore (Inline + Outbox),");
Console.WriteLine("OrionGuard.Testing (DomainEventCapture, InMemoryDispatcher),");
Console.WriteLine("OpenTelemetry instrumentation, W3C trace context across the outbox.");
