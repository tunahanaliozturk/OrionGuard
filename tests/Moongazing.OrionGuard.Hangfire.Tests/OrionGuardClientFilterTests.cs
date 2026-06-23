using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Hangfire.Tests;

public sealed class OrionGuardClientFilterTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddOrionGuard();
        services.AddValidator<CreateUserArgs, CreateUserArgsValidator>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    // Scenario 1: a job whose arguments are valid is created without throwing.
    [Fact]
    public void OnCreating_ValidArguments_DoesNotThrow()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);
        var context = JobContextFactory.Creating(j => j.CreateUser(new CreateUserArgs("user@example.com", "Ada")));

        var exception = Record.Exception(() => filter.OnCreating(context));

        Assert.Null(exception);
    }

    // Scenario 2: a job whose arguments are invalid is rejected at enqueue with the validation error.
    [Fact]
    public void OnCreating_InvalidArguments_ThrowsWithValidationError()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);
        var context = JobContextFactory.Creating(j => j.CreateUser(new CreateUserArgs("not-an-email", "")));

        var exception = Assert.Throws<JobArgumentValidationException>(() => filter.OnCreating(context));

        // Both failing rules are accumulated in a single pass.
        Assert.Equal(2, exception.Errors.Count);
        Assert.Contains(exception.Errors, e => e.ParameterName == "Email");
        Assert.Contains(exception.Errors, e => e.ParameterName == "Name");
        // The structured exception carries the target job identity for diagnostics.
        Assert.Equal(typeof(IJobs), exception.JobType);
        Assert.Equal(nameof(IJobs.CreateUser), exception.MethodName);
    }

    // Scenario 3: an argument whose type has no registered validator passes through untouched.
    [Fact]
    public void OnCreating_ArgumentTypeWithoutValidator_PassesThrough()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);
        var context = JobContextFactory.Creating(j => j.Unvalidated(new UnvalidatedArgs("whatever")));

        var exception = Record.Exception(() => filter.OnCreating(context));

        Assert.Null(exception);
    }

    // Scenario 4: the filter resolves validators from the DI container. Without the registration the
    // same invalid payload is NOT rejected; with it, it is. The only difference is DI resolution.
    [Fact]
    public void OnCreating_ResolvesValidatorsFromDi()
    {
        var withoutValidator = new ServiceCollection().AddOrionGuard().BuildServiceProvider();
        var withValidator = BuildProvider();

        var filterWithout = new OrionGuardClientFilter(withoutValidator);
        var filterWith = new OrionGuardClientFilter(withValidator);

        var invalid = JobContextFactory.Creating(j => j.CreateUser(new CreateUserArgs("not-an-email", "")));

        Assert.Null(Record.Exception(() => filterWithout.OnCreating(
            JobContextFactory.Creating(j => j.CreateUser(new CreateUserArgs("not-an-email", ""))))));
        Assert.Throws<JobArgumentValidationException>(() => filterWith.OnCreating(invalid));

        withoutValidator.Dispose();
        withValidator.Dispose();
    }

    // A job with a mix of validated and unvalidated arguments: only the validated one is enforced.
    [Fact]
    public void OnCreating_MixedArguments_ValidatesOnlyRegisteredTypes()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);

        var valid = JobContextFactory.Creating(j =>
            j.Mixed(new CreateUserArgs("user@example.com", "Ada"), new UnvalidatedArgs("ignored")));
        Assert.Null(Record.Exception(() => filter.OnCreating(valid)));

        var invalid = JobContextFactory.Creating(j =>
            j.Mixed(new CreateUserArgs("bad", ""), new UnvalidatedArgs("ignored")));
        var exception = Assert.Throws<JobArgumentValidationException>(() => filter.OnCreating(invalid));
        Assert.Equal(2, exception.Errors.Count);
    }

    // A parameterless job has no arguments to validate and must pass.
    [Fact]
    public void OnCreating_NoArguments_DoesNotThrow()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);
        var context = JobContextFactory.Creating(j => j.NoArgs());

        Assert.Null(Record.Exception(() => filter.OnCreating(context)));
    }

    // A null argument value is skipped rather than treated as an error or NREd.
    [Fact]
    public void OnCreating_NullArgumentValue_IsSkipped()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);
        var job = new Job(typeof(IJobs), typeof(IJobs).GetMethod(nameof(IJobs.CreateUser))!, new object?[] { null });
        var context = JobContextFactory.Creating(job);

        Assert.Null(Record.Exception(() => filter.OnCreating(context)));
    }

    // A throwing validator surfaces its real exception (unwrapped from the reflection wrapper),
    // not swallowed and not a TargetInvocationException.
    [Fact]
    public void OnCreating_ThrowingValidator_SurfacesRealException()
    {
        using var provider = BuildProvider(s => s.AddTransient<IValidator<UnvalidatedArgs>, ThrowingValidator>());
        var filter = new OrionGuardClientFilter(provider);
        var context = JobContextFactory.Creating(j => j.Unvalidated(new UnvalidatedArgs("x")));

        var exception = Assert.Throws<InvalidOperationException>(() => filter.OnCreating(context));
        Assert.Equal("boom from validator", exception.Message);
    }

    // OnCreated is a no-op: validation is finished by the time OnCreating returns.
    [Fact]
    public void OnCreated_DoesNothing()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);
        var job = Job.FromExpression<IJobs>(j => j.CreateUser(new CreateUserArgs("not-an-email", "")));
        var context = JobContextFactory.Created(job);

        Assert.Null(Record.Exception(() => filter.OnCreated(context)));
    }

    [Fact]
    public void Constructor_NullServiceProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OrionGuardClientFilter(null!));
    }

    [Fact]
    public void OnCreating_NullContext_Throws()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);

        Assert.Throws<ArgumentNullException>(() => filter.OnCreating(null!));
    }
}
