using System.Linq.Expressions;
using Hangfire;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Hangfire.Tests;

/// <summary>Argument carrying a job payload that has a registered validator.</summary>
public sealed record CreateUserArgs(string Email, string Name);

/// <summary>Argument whose type has no registered validator; must pass through untouched.</summary>
public sealed record UnvalidatedArgs(string Anything);

/// <summary>The job "service" whose method expressions stand in for real enqueued work.</summary>
public interface IJobs
{
    void CreateUser(CreateUserArgs args);

    void Mixed(CreateUserArgs first, UnvalidatedArgs second);

    void Unvalidated(UnvalidatedArgs args);

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
