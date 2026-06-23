using System.Linq.Expressions;
using Hangfire;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Hangfire.Tests;

/// <summary>Argument carrying a job payload that has a registered validator.</summary>
public sealed record CreateUserArgs(string Email, string Name);

/// <summary>Argument whose type has no registered validator; must pass through untouched.</summary>
public sealed record UnvalidatedArgs(string Anything);

/// <summary>Argument validated only by an async rule (no synchronous rules at all).</summary>
public sealed record AsyncOnlyArgs(string Token);

/// <summary>Argument whose validator is registered as a DI <c>Scoped</c> service.</summary>
public sealed record ScopedArgs(string Value);

/// <summary>The job "service" whose method expressions stand in for real enqueued work.</summary>
public interface IJobs
{
    void CreateUser(CreateUserArgs args);

    void Mixed(CreateUserArgs first, UnvalidatedArgs second);

    void Unvalidated(UnvalidatedArgs args);

    void AsyncOnly(AsyncOnlyArgs args);

    void Scoped(ScopedArgs args);

    void NoArgs();
}

/// <summary>Validator that the filter must resolve from DI for <see cref="CreateUserArgs"/>.</summary>
public sealed class CreateUserArgsValidator : AbstractValidator<CreateUserArgs>
{
    public CreateUserArgsValidator()
    {
        RuleFor(x => x.Email, "Email", p => p.NotEmpty().Email());
        RuleFor(x => x.Name, "Name", p => p.NotEmpty());
    }
}

/// <summary>A validator whose rule throws, to prove the filter surfaces the real exception.</summary>
public sealed class ThrowingValidator : IValidator<UnvalidatedArgs>
{
    public Core.GuardResult Validate(UnvalidatedArgs value)
        => throw new InvalidOperationException("boom from validator");

    public Task<Core.GuardResult> ValidateAsync(UnvalidatedArgs value, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("boom from validator");
}

/// <summary>
/// Throws from a known helper method so the surfaced stack trace contains a stable, assertable frame.
/// Used to prove the filter preserves the validator's original stack rather than rethrowing from the
/// reflection wrapper.
/// </summary>
public static class ThrowHelper
{
    public const string SentinelMethodName = nameof(BlowUp);

    public static Core.GuardResult BlowUp()
        => throw new InvalidOperationException("boom from validator");
}

/// <summary>
/// Variant of <see cref="ThrowingValidator"/> that throws via <see cref="ThrowHelper"/> so the surfaced
/// stack contains a stable sentinel frame. Both overloads throw <i>synchronously</i> (before returning a
/// Task), exercising the filter's TargetInvocationException-unwrap path with stack preservation.
/// </summary>
public sealed class StackPreservingThrowingValidator : IValidator<UnvalidatedArgs>
{
    public Core.GuardResult Validate(UnvalidatedArgs value) => ThrowHelper.BlowUp();

    public Task<Core.GuardResult> ValidateAsync(UnvalidatedArgs value, CancellationToken cancellationToken = default)
        => Task.FromResult(ThrowHelper.BlowUp());
}

/// <summary>
/// Validator for <see cref="AsyncOnlyArgs"/> that declares <b>only</b> an async rule (via
/// <c>RuleForAsync</c>). Under the synchronous <c>Validate(T)</c> overload this validator is a no-op, so
/// it proves the filter runs the async pipeline at enqueue time. The token "valid" passes; anything else
/// is rejected.
/// </summary>
public sealed class AsyncOnlyArgsValidator : AbstractValidator<AsyncOnlyArgs>
{
    public AsyncOnlyArgsValidator()
    {
        RuleForAsync(
            async x =>
            {
                await Task.Yield();
                return x.Token == "valid";
            },
            "Token failed async validation.",
            "Token");
    }
}

/// <summary>
/// A scoped dependency. Resolving a <c>Scoped</c> service directly from the root
/// <see cref="IServiceProvider"/> throws; it only succeeds inside a DI scope. That makes a successful
/// validation a positive proof the filter resolved from a created scope, not the root provider.
/// </summary>
public sealed class ScopedDependency
{
    public Guid InstanceId { get; } = Guid.NewGuid();
}

/// <summary>
/// Scoped validator for <see cref="ScopedArgs"/>. It takes a <see cref="ScopedDependency"/> via
/// constructor injection so it can only be constructed within a scope. It records, into a shared sink,
/// the dependency instance it saw on each invocation so a test can assert that separate enqueues used
/// separate scopes.
/// </summary>
public sealed class ScopedArgsValidator : AbstractValidator<ScopedArgs>
{
    public ScopedArgsValidator(ScopedDependency dependency, ScopeObservationSink sink)
    {
        sink.Record(dependency.InstanceId);
        RuleFor(x => x.Value, "Value", p => p.NotEmpty());
    }
}

/// <summary>Collects the scoped-dependency instance ids observed across validator activations.</summary>
public sealed class ScopeObservationSink
{
    private readonly List<Guid> _observed = new();

    public void Record(Guid instanceId) => _observed.Add(instanceId);

    public IReadOnlyList<Guid> Observed => _observed;
}

/// <summary>
/// <see cref="IServiceScopeFactory"/> decorator that counts how many scopes were created and exposes the
/// per-scope <see cref="IServiceProvider"/> instances, so a test can assert the filter opens a fresh
/// scope per <c>OnCreating</c> invocation.
/// </summary>
public sealed class CountingScopeFactory : IServiceScopeFactory
{
    private readonly IServiceScopeFactory _inner;
    private readonly List<IServiceProvider> _scopeProviders = new();

    public CountingScopeFactory(IServiceScopeFactory inner) => _inner = inner;

    public int CreatedScopeCount { get; private set; }

    public IReadOnlyList<IServiceProvider> ScopeProviders => _scopeProviders;

    public IServiceScope CreateScope()
    {
        var scope = _inner.CreateScope();
        CreatedScopeCount++;
        _scopeProviders.Add(scope.ServiceProvider);
        return scope;
    }
}

/// <summary>
/// Helpers to build a real Hangfire <see cref="CreatingContext"/> from a job expression, using a fake
/// storage. This drives the client-filter pipeline (<c>OnCreating</c>) with the exact
/// <see cref="Job"/>/<see cref="CreatingContext"/> types Hangfire uses, without a running server.
/// </summary>
public static class JobContextFactory
{
    public static CreatingContext Creating(Expression<Action<IJobs>> methodCall)
    {
        var job = Job.FromExpression(methodCall);
        return Creating(job);
    }

    public static CreatingContext Creating(Job job)
    {
        var storage = new FakeJobStorage();
        var create = new CreateContext(storage, new FakeStorageConnection(), job, new EnqueuedState());
        return new CreatingContext(create);
    }

    public static CreatedContext Created(Job job)
    {
        var storage = new FakeJobStorage();
        var create = new CreateContext(storage, new FakeStorageConnection(), job, new EnqueuedState());
        return new CreatedContext(create, backgroundJob: null, canceled: false, exception: null);
    }
}

/// <summary>Minimal <see cref="JobStorage"/> the filter never actually reads from.</summary>
public sealed class FakeJobStorage : JobStorage
{
    public override IMonitoringApi GetMonitoringApi() => throw new NotSupportedException();

    public override IStorageConnection GetConnection() => new FakeStorageConnection();
}

/// <summary>
/// No-op <see cref="IStorageConnection"/>. The OrionGuard client filter only reads
/// <c>context.Job.Args</c> in <c>OnCreating</c> and never touches the connection, so every member
/// throws to make any accidental use during the test obvious.
/// </summary>
public sealed class FakeStorageConnection : IStorageConnection
{
    public IDisposable AcquireDistributedLock(string resource, TimeSpan timeout) => throw new NotSupportedException();

    public void AnnounceServer(string serverId, ServerContext context) => throw new NotSupportedException();

    public string CreateExpiredJob(Job job, IDictionary<string, string> parameters, DateTime createdAt, TimeSpan expireIn) => throw new NotSupportedException();

    public IWriteOnlyTransaction CreateWriteTransaction() => throw new NotSupportedException();

    public void Dispose() { }

    public IFetchedJob FetchNextJob(string[] queues, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Dictionary<string, string> GetAllEntriesFromHash(string key) => throw new NotSupportedException();

    public HashSet<string> GetAllItemsFromSet(string key) => throw new NotSupportedException();

    public string GetFirstByLowestScoreFromSet(string key, double fromScore, double toScore) => throw new NotSupportedException();

    public JobData GetJobData(string jobId) => throw new NotSupportedException();

    public string GetJobParameter(string id, string name) => throw new NotSupportedException();

    public StateData GetStateData(string jobId) => throw new NotSupportedException();

    public string GetValueFromHash(string key, string name) => throw new NotSupportedException();

    public void Heartbeat(string serverId) => throw new NotSupportedException();

    public void RemoveServer(string serverId) => throw new NotSupportedException();

    public int RemoveTimedOutServers(TimeSpan timeOut) => throw new NotSupportedException();

    public void SetJobParameter(string id, string name, string value) => throw new NotSupportedException();

    public void SetRangeInHash(string key, IEnumerable<KeyValuePair<string, string>> keyValuePairs) => throw new NotSupportedException();
}
