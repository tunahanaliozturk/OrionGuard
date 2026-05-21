using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Locking;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.Outbox.Locking;

public class SkipLockedDistributedLockConcurrencyTests : IAsyncLifetime
{
    private LockingTestFixture _fx = default!;

    public Task InitializeAsync() { _fx = new LockingTestFixture(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await _fx.DisposeAsync();

    [Fact]
    public async Task TryAcquireAsync_ShouldHandOutExactlyOneHandle_AcrossFiveParallelCallers()
    {
        var @lock = new SkipLockedDistributedLock(
            _fx.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SkipLockedDistributedLock>.Instance);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => @lock.TryAcquireAsync("k", TimeSpan.FromSeconds(30)))
            .ToArray();

        var handles = await Task.WhenAll(tasks);

        Assert.Equal(1, handles.Count(h => h is not null));
        Assert.Equal(4, handles.Count(h => h is null));
    }
}
