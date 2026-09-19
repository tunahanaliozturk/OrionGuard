using System.Collections.ObjectModel;
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

/// <summary>
/// Every rule is async, so <c>Validate</c> reports nothing at all for this validator and only
/// <c>ValidateAsync</c> sees its rules.
/// </summary>
public sealed class AsyncOnlyValidator : AbstractValidator<CreateUserRequest>
{
    public AsyncOnlyValidator()
        => RuleForAsync(
            (request, _, _) => Task.FromResult(!string.IsNullOrWhiteSpace(request.Email)),
            "Email is required.",
            nameof(CreateUserRequest.Email))
            .WithErrorCode("EMAIL_ASYNC");
}

/// <summary>One property covered by a sync rule, another only by an async one.</summary>
public sealed class MixedRulesValidator : AbstractValidator<CreateUserRequest>
{
    public MixedRulesValidator()
    {
        RuleFor(request => request.Name, nameof(CreateUserRequest.Name), property => property
            .NotNull("Name is required."))
            .WithErrorCode("NAME");

        RuleForAsync(
            request => Task.FromResult(request.Age > 0),
            "Age must be greater than 0.",
            nameof(CreateUserRequest.Age))
            .WithErrorCode("AGE_ASYNC");
    }
}

/// <summary>
/// Collection properties whose declared types accept neither an array nor a <c>List&lt;T&gt;</c>,
/// plus one nothing can be constructed for.
/// </summary>
public sealed class TagsRequest
{
    public HashSet<string> Tags { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
    public ReadOnlyCollection<string>? Items { get; set; }
}

/// <summary>Rules that accept null but reject an empty collection -- invisible without a probe value of the declared type.</summary>
public sealed class TagsValidator : AbstractValidator<TagsRequest>
{
    public TagsValidator()
    {
        RuleFor(request => request.Tags is null || request.Tags.Count > 0,
            "Tags must not be empty when supplied.", nameof(TagsRequest.Tags))
            .WithErrorCode("TAGS");

        RuleFor(request => request.Metadata is null || request.Metadata.Count > 0,
            "Metadata must not be empty when supplied.", nameof(TagsRequest.Metadata))
            .WithErrorCode("META");

        RuleFor(request => request.Items is not null, "Items is required.", nameof(TagsRequest.Items))
            .WithErrorCode("ITEMS");
    }
}
