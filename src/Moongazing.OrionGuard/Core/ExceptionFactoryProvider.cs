namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Provides the global exception factory for OrionGuard.
/// Set a custom factory via Configure() or use the default.
/// </summary>
public static class ExceptionFactoryProvider
{
    private static volatile IExceptionFactory _factory = DefaultExceptionFactory.Instance;

    /// <summary>Current exception factory.</summary>
    public static IExceptionFactory Current => _factory;

    /// <summary>Set a custom exception factory.</summary>
    public static void Configure(IExceptionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Reset to default factory.</summary>
    public static void Reset() => _factory = DefaultExceptionFactory.Instance;
}
