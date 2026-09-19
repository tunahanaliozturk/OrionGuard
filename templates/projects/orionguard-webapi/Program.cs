using Moongazing.OrionGuard.AspNetCore.Extensions;
using Moongazing.OrionGuard.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrionGuardAspNetCore();

// AddOrionGuardAspNetCore does not scan assemblies, so every validator is registered by hand.
builder.Services.AddValidator<CreateCustomer, CreateCustomerValidator>();

builder.Services.AddHealthChecks().AddOrionGuardCheck();

var app = builder.Build();

// Turns GuardException and AggregateValidationException into RFC 9457 ProblemDetails responses.
app.UseOrionGuardValidation();

app.MapHealthChecks("/health");

// WithValidation runs IValidator<CreateCustomer> before the handler; an invalid body never reaches it.
app.MapPost("/customers", (CreateCustomer request) => Results.Ok(request))
   .WithValidation<CreateCustomer>();

app.Run();

public sealed record CreateCustomer(string Email, string DisplayName);

public sealed class CreateCustomerValidator : AbstractValidator<CreateCustomer>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.Email, nameof(CreateCustomer.Email), p => p.NotEmpty().Email());
        RuleFor(x => x.DisplayName, nameof(CreateCustomer.DisplayName), p => p.NotEmpty().Length(2, 60));
    }
}
