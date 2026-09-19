namespace Moongazing.OrionGuard.Tests;

/// <summary>
/// Stands in for a scoped DbContext shared by several validators: it throws when a second operation
/// starts before the previous one has completed, the way EF Core does.
/// </summary>
internal sealed class SingleOperationLookup
{
    private int inUse;

    public async Task<bool> IsFreeAsync(string code)
    {
        if (Interlocked.Exchange(ref inUse, 1) == 1)
        {
            throw new InvalidOperationException("A second operation was started before a previous operation completed.");
        }

        try
        {
            await Task.Delay(20);
            return code != "taken";
        }
        finally
        {
            Volatile.Write(ref inUse, 0);
        }
    }
}
