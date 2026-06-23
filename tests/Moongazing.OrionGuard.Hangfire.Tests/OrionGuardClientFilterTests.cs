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
        services.AddValidator<AsyncOnlyArgs, AsyncOnlyArgsValidator>();
        configure?.Invoke(services);
        // Validate scope usage so that resolving a scoped service from the root provider (the captive
        // dependency bug this PR fixes) throws at build/resolve time instead of silently "working".
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
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
        Assert.Throws<ArgumentNullException>(() => new OrionGuardClientFilter((IServiceProvider)null!));
    }

    [Fact]
    public void OnCreating_NullContext_Throws()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);

        Assert.Throws<ArgumentNullException>(() => filter.OnCreating(null!));
    }

    // A scoped validator (with a scoped dependency) is resolved from a per-invocation scope, NOT the root
    // provider. Two enqueues open two distinct scopes, each producing a distinct scoped-dependency
    // instance. Because the provider is built with ValidateScopes=true, resolving the scoped validator
    // from the root would throw -- so a clean run is itself proof the filter scoped correctly.
    [Fact]
    public void OnCreating_ScopedValidator_ResolvedFromFreshScopePerEnqueue_NotRoot()
    {
        var sink = new ScopeObservationSink();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(sink);
            s.AddScoped<ScopedDependency>();
            s.AddScoped<IValidator<ScopedArgs>, ScopedArgsValidator>();
        });

        // Decorate the real scope factory so we can assert a new scope is created per enqueue.
        var countingFactory = new CountingScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var filter = new OrionGuardClientFilter(countingFactory);

        filter.OnCreating(JobContextFactory.Creating(j => j.Scoped(new ScopedArgs("ok"))));
        filter.OnCreating(JobContextFactory.Creating(j => j.Scoped(new ScopedArgs("ok"))));

        // Two enqueues -> two scopes created.
        Assert.Equal(2, countingFactory.CreatedScopeCount);
        Assert.Equal(2, countingFactory.ScopeProviders.Distinct().Count());

        // The scoped dependency was activated twice with two different instances: a captured-root
        // resolution would either throw (ValidateScopes) or reuse one instance.
        Assert.Equal(2, sink.Observed.Count);
        Assert.Equal(2, sink.Observed.Distinct().Count());
    }

    // A scoped validator that rejects: the failure still surfaces as JobArgumentValidationException, and
    // resolution from the scope (not root) is exercised on the failing path too.
    [Fact]
    public void OnCreating_ScopedValidator_InvalidArgument_IsRejected()
    {
        var sink = new ScopeObservationSink();
        using var provider = BuildProvider(s =>
        {
            s.AddSingleton(sink);
            s.AddScoped<ScopedDependency>();
            s.AddScoped<IValidator<ScopedArgs>, ScopedArgsValidator>();
        });
        var filter = new OrionGuardClientFilter(provider);

        var context = JobContextFactory.Creating(j => j.Scoped(new ScopedArgs("")));

        var exception = Assert.Throws<JobArgumentValidationException>(() => filter.OnCreating(context));
        Assert.Contains(exception.Errors, e => e.ParameterName == "Value");
    }

    // A validator that declares ONLY an async rule (RuleForAsync) must still be enforced at enqueue.
    // The synchronous Validate(T) overload would treat it as a no-op, so this proves the filter runs the
    // async pipeline (ValidateAsync) and blocks on it.
    [Fact]
    public void OnCreating_AsyncOnlyValidator_InvalidArgument_IsRejected()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);

        var context = JobContextFactory.Creating(j => j.AsyncOnly(new AsyncOnlyArgs("not-valid")));

        var exception = Assert.Throws<JobArgumentValidationException>(() => filter.OnCreating(context));
        Assert.Single(exception.Errors);
        Assert.Equal("Token", exception.Errors[0].ParameterName);
    }

    // The async-only validator passes a payload its async rule accepts.
    [Fact]
    public void OnCreating_AsyncOnlyValidator_ValidArgument_DoesNotThrow()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);

        var context = JobContextFactory.Creating(j => j.AsyncOnly(new AsyncOnlyArgs("valid")));

        Assert.Null(Record.Exception(() => filter.OnCreating(context)));
    }

    // ToString() must not drop the exception type or stack trace: it augments the standard exception
    // report (type + message + stack) with the per-argument validation breakdown.
    [Fact]
    public void JobArgumentValidationException_ToString_ContainsTypeNameAndStackAndDetails()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider);
        var context = JobContextFactory.Creating(j => j.CreateUser(new CreateUserArgs("not-an-email", "")));

        var exception = Assert.Throws<JobArgumentValidationException>(() => filter.OnCreating(context));
        var text = exception.ToString();

        // Type name is present (was dropped before the fix).
        Assert.Contains(typeof(JobArgumentValidationException).FullName!, text);
        // Stack trace is present: the throwing frame in the filter shows up once the exception is thrown.
        Assert.Contains(nameof(OrionGuardClientFilter.OnCreating), text);
        Assert.Contains("OrionGuardClientFilter", text);
        // The validation breakdown is still included.
        Assert.Contains("[Email]", text);
        Assert.Contains("[Name]", text);
    }

    // A throwing validator surfaces with its ORIGINAL stack trace (the rule's frame), not a stack that
    // starts at the reflection wrapper. Proven by asserting the sentinel helper frame is present.
    [Fact]
    public void OnCreating_ThrowingValidator_PreservesOriginalStackTrace()
    {
        using var provider = BuildProvider(s =>
            s.AddTransient<IValidator<UnvalidatedArgs>, StackPreservingThrowingValidator>());
        var filter = new OrionGuardClientFilter(provider);
        var context = JobContextFactory.Creating(j => j.Unvalidated(new UnvalidatedArgs("x")));

        var exception = Assert.Throws<InvalidOperationException>(() => filter.OnCreating(context));

        Assert.Equal("boom from validator", exception.Message);
        Assert.NotNull(exception.StackTrace);
        // The original throwing frame is preserved (ExceptionDispatchInfo), not erased by a rethrow.
        Assert.Contains(ThrowHelper.SentinelMethodName, exception.StackTrace!);
    }

    // The filter can be constructed directly from an IServiceScopeFactory (the lifetime-correct primary
    // ctor) and validates normally.
    [Fact]
    public void OnCreating_ConstructedFromScopeFactory_Validates()
    {
        using var provider = BuildProvider();
        var filter = new OrionGuardClientFilter(provider.GetRequiredService<IServiceScopeFactory>());

        var context = JobContextFactory.Creating(j => j.CreateUser(new CreateUserArgs("not-an-email", "")));

        Assert.Throws<JobArgumentValidationException>(() => filter.OnCreating(context));
    }

    [Fact]
    public void Constructor_NullScopeFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OrionGuardClientFilter((IServiceScopeFactory)null!));
    }
}
