using Company.Outbox;
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionGuard.AspNetCore.Extensions;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrionGuardAspNetCore();
builder.Services.AddValidator<PlaceOrder, PlaceOrderValidator>();
builder.Services.AddHealthChecks().AddOrionGuardCheck();

builder.Services.AddOrionGuardDomainEvents();
builder.Services.AddOrionGuardDomainEventHandlers(typeof(Program).Assembly);

// Outbox mode: SaveChanges writes each raised event as a row in the same transaction as the
// aggregate, and AddOrionGuardEfCore registers the hosted service that delivers those rows.
builder.Services.AddOrionGuardEfCore<OrderDbContext>(o => o.UseOutbox());

// Set ConnectionStrings:Orders in configuration to point at your own database.
var connectionString = builder.Configuration.GetConnectionString("Orders")
#if (database == "sqlite")
    ?? "Data Source=orders.db";
#endif
#if (database == "sqlserver")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=Orders;Trusted_Connection=True;TrustServerCertificate=True";
#endif
#if (database == "postgres")
    ?? "Host=localhost;Database=orders;Username=postgres;Password=postgres";
#endif

// The (sp, options) overload lets the interceptor resolve its collaborators from the DbContext's
// own scope instead of the root provider.
builder.Services.AddDbContext<OrderDbContext>((sp, options) => options
#if (database == "sqlite")
    .UseSqlite(connectionString)
#endif
#if (database == "sqlserver")
    .UseSqlServer(connectionString)
#endif
#if (database == "postgres")
    .UseNpgsql(connectionString)
#endif
    .UseOrionGuardDomainEvents(sp));

var app = builder.Build();

// Sample convenience. A real deployment applies an EF Core migration instead, so that the outbox
// and lock tables are versioned with the rest of the schema.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.EnsureCreatedAsync();
}

app.UseOrionGuardValidation();

app.MapHealthChecks("/health");

app.MapPost("/orders", async (PlaceOrder request, OrderDbContext db) =>
{
    var order = Order.Place(request.Sku, request.Quantity);
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    return Results.Ok(new { order.Id });
}).WithValidation<PlaceOrder>();

app.Run();
