using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Testing.Tests;

/// <summary>
/// Shared fixtures for the snapshot and coverage tests. They are top-level types because both test
/// classes use them, and because the committed snapshot is named after <see cref="CreateUserValidator"/>.
/// </summary>
public sealed class CreateUserRequest
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public int Age { get; set; }
    public string? Notes { get; set; }
}

public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(request => request.Email, nameof(CreateUserRequest.Email), property => property
            .NotNull("Email is required.")
            .NotEmpty("Email is required.")
            .Email("Email must be a valid address."))
            .WithErrorCode("EMAIL");

        RuleFor(request => request.Name, nameof(CreateUserRequest.Name), property => property
            .NotNull("Name is required.")
            .Length(2, 50, "Name must be 2 to 50 characters."))
            .WithErrorCode("NAME");

        RuleFor(request => request.Age, nameof(CreateUserRequest.Age), property => property
            .GreaterThan(0, "Age must be greater than 0."))
            .WithErrorCode("AGE");
    }
}

/// <summary>A validator whose only rule no probe value can make fail, so the sweep never sees it.</summary>
public sealed class NeverFailingValidator : AbstractValidator<CreateUserRequest>
{
    public NeverFailingValidator()
        => RuleFor(request => request.Age != 42, "Age must not be 42.", nameof(CreateUserRequest.Age));
}

/// <summary>A model whose rules come from both sources: an attribute and a validator.</summary>
public sealed class TokenRequest
{
    [NotNull]
    public string? Token { get; set; }

    public string? Scope { get; set; }
}

public sealed class TokenValidator : AbstractValidator<TokenRequest>
{
    public TokenValidator()
        => RuleFor(request => request.Scope, nameof(TokenRequest.Scope), property => property
            .NotNull("Scope is required."))
            .WithErrorCode("SCOPE");
}
