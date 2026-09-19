using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.SignalR.Tests;

public sealed class OrionGuardHubFilterTests
{
    public sealed record ChatMessage(string Text);

    public sealed class ChatHub : Hub
    {
        public Task Send(ChatMessage message) => Task.CompletedTask;
    }

    private sealed class ChatMessageValidator : AbstractValidator<ChatMessage>
    {
        public ChatMessageValidator()
        {
            RuleFor(m => !string.IsNullOrEmpty(m.Text), "Text is required.", "Text");
        }
    }

    private sealed class NoLinksValidator : AbstractValidator<ChatMessage>
    {
        public NoLinksValidator()
        {
            RuleFor(m => !m.Text.Contains("http", StringComparison.Ordinal), "Links are not allowed.", "Text");
        }
    }

    private sealed class AsyncOnlyValidator : AbstractValidator<ChatMessage>
    {
        public AsyncOnlyValidator()
        {
            RuleForAsync(async m =>
            {
                await Task.Yield();
                return !m.Text.Contains("spam", StringComparison.Ordinal);
            }, "Message looks like spam.", "Text");
        }
    }

    private sealed class BannedWords
    {
        public bool Contains(string text) => text.Contains("banned", StringComparison.Ordinal);
    }

    private sealed class BannedWordsValidator : IValidator<ChatMessage>
    {
        private readonly BannedWords bannedWords;

        public BannedWordsValidator(BannedWords bannedWords)
        {
            this.bannedWords = bannedWords;
        }

        public GuardResult Validate(ChatMessage value) =>
            bannedWords.Contains(value.Text)
                ? GuardResult.Failure("Text", "Text contains a banned word.")
                : GuardResult.Success();

        public Task<GuardResult> ValidateAsync(ChatMessage value, CancellationToken cancellationToken = default) =>
            Task.FromResult(Validate(value));
    }

    private sealed class TestHubCallerContext : HubCallerContext
    {
        public override string ConnectionId => "connection-1";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort()
        {
        }
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        // Scope validation makes a scoped validator resolved from the root provider throw.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<(object? Result, bool NextCalled)> InvokeAsync(
        ServiceProvider root,
        IServiceProvider invocationServices,
        ChatMessage message)
    {
        // The filter is a singleton built from the root provider, exactly as AddOrionGuardSignalR registers it.
        var filter = new OrionGuardHubFilter(root);
        var context = new HubInvocationContext(
            new TestHubCallerContext(),
            invocationServices,
            new ChatHub(),
            typeof(ChatHub).GetMethod(nameof(ChatHub.Send))!,
            new object?[] { message });
        var nextCalled = false;

        var result = await filter.InvokeMethodAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("sent");
        });

        return (result, nextCalled);
    }

    [Fact]
    public async Task InvokeMethodAsync_rejects_an_invalid_argument_with_a_HubException()
    {
        using var provider = BuildProvider(s => s.AddValidator<ChatMessage, ChatMessageValidator>());
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<HubException>(
            () => InvokeAsync(provider, scope.ServiceProvider, new ChatMessage("")));

        Assert.Equal("Validation failed: Text: Text is required.", exception.Message);
    }

    [Fact]
    public async Task InvokeMethodAsync_calls_the_hub_method_when_the_argument_is_valid()
    {
        using var provider = BuildProvider(s => s.AddValidator<ChatMessage, ChatMessageValidator>());
        using var scope = provider.CreateScope();

        var (result, nextCalled) = await InvokeAsync(provider, scope.ServiceProvider, new ChatMessage("hello"));

        Assert.True(nextCalled);
        Assert.Equal("sent", result);
    }

    [Fact]
    public async Task InvokeMethodAsync_enforces_async_rules()
    {
        using var provider = BuildProvider(s => s.AddValidator<ChatMessage, AsyncOnlyValidator>());
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<HubException>(
            () => InvokeAsync(provider, scope.ServiceProvider, new ChatMessage("spam")));

        Assert.Equal("Validation failed: Text: Message looks like spam.", exception.Message);
    }

    [Fact]
    public async Task InvokeMethodAsync_runs_every_registered_validator_and_lists_errors_in_registration_order()
    {
        using var provider = BuildProvider(s => s
            .AddValidator<ChatMessage, NoLinksValidator>()
            .AddValidator<ChatMessage, AsyncOnlyValidator>());
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<HubException>(
            () => InvokeAsync(provider, scope.ServiceProvider, new ChatMessage("http://spam")));

        Assert.Equal(
            "Validation failed: Text: Links are not allowed.; Text: Message looks like spam.",
            exception.Message);
    }

    [Fact]
    public async Task InvokeMethodAsync_resolves_scoped_validators_from_the_invocation_scope()
    {
        using var provider = BuildProvider(s =>
        {
            s.AddScoped<BannedWords>();
            s.AddScoped<IValidator<ChatMessage>, BannedWordsValidator>();
        });
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<HubException>(
            () => InvokeAsync(provider, scope.ServiceProvider, new ChatMessage("a banned word")));
        var (_, nextCalled) = await InvokeAsync(provider, scope.ServiceProvider, new ChatMessage("hello"));

        Assert.Equal("Validation failed: Text: Text contains a banned word.", exception.Message);
        Assert.True(nextCalled);
    }
}
