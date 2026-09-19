using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.MassTransit.Tests;

public sealed class OrionGuardConsumeFilterTests
{
    /// <summary>
    /// Builds a provider with the MassTransit in-memory test harness. By default the filter is applied at
    /// the bus level, the way the README shows it; <paramref name="configureTransport"/> replaces that.
    /// </summary>
    private static ServiceProvider BuildProvider(
        Action<IBusRegistrationContext, IInMemoryBusFactoryConfigurator>? configureTransport = null)
    {
        var services = new ServiceCollection();
        services.AddOrionGuard();
        services.AddValidator<SubmitOrder, SubmitOrderValidator>();
        services.AddValidator<AsyncOnlyMessage, AsyncOnlyMessageValidator>();
        services.AddValidator<MultiValidatedMessage, FirstFieldValidator>();
        services.AddValidator<MultiValidatedMessage, SecondFieldValidator>();
        services.AddValidator<IShipOrder, ShipOrderValidator>();
        services.AddScoped<IValidator<ScopedMessage>, ScopedMessageValidator>();
        services.AddScoped<ScopedDependency>();
        services.AddSingleton<InvocationCounter>();
        services.AddSingleton<ScopeLog>();

        services.AddMassTransitTestHarness(x =>
        {
            x.SetTestTimeouts(testInactivityTimeout: TimeSpan.FromSeconds(1));
            x.AddConsumer<SubmitOrderConsumer>();
            x.AddConsumer<UnvalidatedMessageConsumer>();
            x.AddConsumer<AsyncOnlyMessageConsumer>();
            x.AddConsumer<ScopedMessageConsumer>();
            x.AddConsumer<MultiValidatedMessageConsumer>();
            x.AddConsumer<ShipOrderConsumer>();
            x.UsingInMemory(configureTransport ?? ((context, cfg) =>
            {
                cfg.UseOrionGuardValidation(context);
                cfg.ConfigureEndpoints(context);
            }));
        });

        // Why: with ValidateScopes on, resolving a scoped validator or dependency from the root provider
        // throws, so a passing scoped test proves resolution happened inside a scope.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<ITestHarness> StartHarnessAsync(IServiceProvider provider)
    {
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    /// <summary>
    /// The exception the consume pipe faulted with. The bus-level consume observer records it even when
    /// the consumer never ran.
    /// </summary>
    private static async Task<MessageValidationException> GetValidationFaultAsync<TMessage>(ITestHarness harness)
        where TMessage : class
    {
        Assert.True(await harness.Consumed.Any<TMessage>(m => m.Exception is not null));
        var faulted = harness.Consumed.Select<TMessage>(m => m.Exception is not null).Single();
        return Assert.IsType<MessageValidationException>(faulted.Exception);
    }

    [Fact]
    public async Task ValidMessage_ReachesConsumer()
    {
        await using var provider = BuildProvider();
        var harness = await StartHarnessAsync(provider);

        await harness.Bus.Publish(new SubmitOrder("A-1", "user@example.com"));

        Assert.True(await harness.GetConsumerHarness<SubmitOrderConsumer>().Consumed.Any<SubmitOrder>());
        Assert.False(await harness.Consumed.Any<SubmitOrder>(m => m.Exception is not null));
    }

    [Fact]
    public async Task InvalidMessage_NeverReachesConsumer_AndFaultsWithEveryError()
    {
        await using var provider = BuildProvider();
        var harness = await StartHarnessAsync(provider);

        await harness.Bus.Publish(new SubmitOrder("", "not-an-email"));

        var exception = await GetValidationFaultAsync<SubmitOrder>(harness);
        Assert.Equal(typeof(SubmitOrder), exception.MessageType);
        Assert.Equal(2, exception.Errors.Count);
        Assert.Contains(exception.Errors, e => e.ParameterName == "OrderId");
        Assert.Contains(exception.Errors, e => e.ParameterName == "CustomerEmail");
        Assert.False(await harness.GetConsumerHarness<SubmitOrderConsumer>().Consumed.Any<SubmitOrder>());
        // No consumer ran, so MassTransit reports a ReceiveFault rather than a Fault<SubmitOrder>.
        Assert.True(await harness.Published.Any<ReceiveFault>());
        Assert.False(await harness.Published.Any<Fault<SubmitOrder>>());
    }

    // Also proves the extension works on a single receive endpoint, not only on the bus.
    [Fact]
    public async Task InvalidMessage_OnEndpointLevelFilter_IsMovedToErrorQueue()
    {
        var movedHeaders = new TaskCompletionSource<Headers>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = BuildProvider((context, cfg) =>
        {
            cfg.ReceiveEndpoint("submit-order", e =>
            {
                e.UseOrionGuardValidation(context);
                e.ConfigureConsumer<SubmitOrderConsumer>(context);
            });

            // Reads the error queue without the filter and captures the fault header MassTransit adds on move.
            cfg.ReceiveEndpoint("submit-order_error", e =>
            {
                e.ConfigureConsumeTopology = false;
                e.Handler<SubmitOrder>(moved =>
                {
                    movedHeaders.TrySetResult(moved.ReceiveContext.TransportHeaders);
                    return Task.CompletedTask;
                });
            });
        });
        var harness = await StartHarnessAsync(provider);

        var endpoint = await harness.Bus.GetSendEndpoint(new Uri("queue:submit-order"));
        await endpoint.Send(new SubmitOrder("", "not-an-email"));

        var headers = await movedHeaders.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(typeof(MessageValidationException).FullName, headers.Get<string>(MessageHeaders.FaultExceptionType));
        Assert.Equal(
            $"Message validation failed for '{typeof(SubmitOrder).FullName}' with 2 error(s).",
            headers.Get<string>(MessageHeaders.FaultMessage));
        Assert.False(await harness.GetConsumerHarness<SubmitOrderConsumer>().Consumed.Any<SubmitOrder>());
    }

    [Fact]
    public async Task AsyncOnlyRule_IsEnforced()
    {
        await using var provider = BuildProvider();
        var harness = await StartHarnessAsync(provider);

        // Both are published before waiting: once the harness has gone inactive it stops waiting.
        await harness.Bus.Publish(new AsyncOnlyMessage("nope"));
        await harness.Bus.Publish(new AsyncOnlyMessage("valid"));

        var exception = await GetValidationFaultAsync<AsyncOnlyMessage>(harness);
        Assert.Contains(exception.Errors, e => e.ParameterName == "Token");
        var consumed = Assert.Single(harness.GetConsumerHarness<AsyncOnlyMessageConsumer>().Consumed.Select<AsyncOnlyMessage>());
        Assert.Equal("valid", consumed.Context.Message.Token);
    }

    [Fact]
    public async Task ScopedValidator_IsResolvedFromTheConsumeScope()
    {
        await using var provider = BuildProvider();
        var harness = await StartHarnessAsync(provider);

        await harness.Bus.Publish(new ScopedMessage("value"));

        Assert.True(await harness.GetConsumerHarness<ScopedMessageConsumer>().Consumed.Any<ScopedMessage>());
        var log = provider.GetRequiredService<ScopeLog>();
        var validatorInstance = Assert.Single(log.ValidatorInstances);
        var consumerInstance = Assert.Single(log.ConsumerInstances);
        // The validator and the consumer saw the same scoped instance: one consume scope for both.
        Assert.Equal(consumerInstance, validatorInstance);
    }

    [Fact]
    public async Task MultipleValidators_AllRun_AndTheirErrorsAreCombined()
    {
        await using var provider = BuildProvider();
        var harness = await StartHarnessAsync(provider);

        await harness.Bus.Publish(new MultiValidatedMessage("", ""));

        var exception = await GetValidationFaultAsync<MultiValidatedMessage>(harness);
        Assert.Equal(new[] { "First", "Second" }, exception.Errors.Select(e => e.ParameterName));
        Assert.False(await harness.GetConsumerHarness<MultiValidatedMessageConsumer>().Consumed.Any<MultiValidatedMessage>());
    }

    [Fact]
    public async Task MessageTypeWithoutValidator_PassesThrough()
    {
        await using var provider = BuildProvider();
        var harness = await StartHarnessAsync(provider);

        await harness.Bus.Publish(new UnvalidatedMessage("anything"));

        Assert.True(await harness.GetConsumerHarness<UnvalidatedMessageConsumer>().Consumed.Any<UnvalidatedMessage>());
        Assert.False(await harness.Consumed.Any<UnvalidatedMessage>(m => m.Exception is not null));
    }

    // MassTransit deserializes an interface contract into a generated type, so a lookup keyed on
    // context.Message.GetType() would find no validator and let this invalid message through.
    [Fact]
    public async Task InterfaceContract_IsValidatedWithTheContractValidator()
    {
        await using var provider = BuildProvider();
        var harness = await StartHarnessAsync(provider);

        await harness.Bus.Publish<IShipOrder>(new { OrderId = "" });

        var exception = await GetValidationFaultAsync<IShipOrder>(harness);
        Assert.Equal(typeof(IShipOrder), exception.MessageType);
        Assert.Contains(exception.Errors, e => e.ParameterName == "OrderId");
        Assert.False(await harness.GetConsumerHarness<ShipOrderConsumer>().Consumed.Any<IShipOrder>());
    }

    // Pins the README guidance: bus-level message retry wraps the filter, so without Ignore a
    // deterministic validation failure is re-validated on every attempt.
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 4)]
    public async Task MessageRetry_RevalidatesUnlessValidationFailuresAreIgnored(bool ignoreValidationFailures, int expectedValidations)
    {
        await using var provider = BuildProvider((context, cfg) =>
        {
            cfg.UseMessageRetry(r =>
            {
                r.Immediate(3);
                if (ignoreValidationFailures)
                {
                    r.Ignore<MessageValidationException>();
                }
            });
            cfg.UseOrionGuardValidation(context);
            cfg.ConfigureEndpoints(context);
        });
        var harness = await StartHarnessAsync(provider);

        await harness.Bus.Publish(new SubmitOrder("", "not-an-email"));

        // ReceiveFault is published once, after the last attempt has failed.
        Assert.True(await harness.Published.Any<ReceiveFault>());
        Assert.Equal(expectedValidations, provider.GetRequiredService<InvocationCounter>().Count);
    }
}
