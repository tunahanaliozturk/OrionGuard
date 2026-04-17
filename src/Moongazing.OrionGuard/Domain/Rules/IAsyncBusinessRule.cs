using System.Threading;
using System.Threading.Tasks;

namespace Moongazing.OrionGuard.Domain.Rules;

/// <summary>
/// An asynchronous business rule — useful when rule evaluation requires I/O (e.g., uniqueness
/// checks against a repository).
/// </summary>
public interface IAsyncBusinessRule
{
    Task<bool> IsBrokenAsync(CancellationToken cancellationToken = default);

    string MessageKey { get; }
    string DefaultMessage { get; }
    object[]? MessageArgs => null;
}
