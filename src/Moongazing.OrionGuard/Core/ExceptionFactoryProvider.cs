namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Holds a process-wide <see cref="IExceptionFactory"/>.
/// </summary>
/// <remarks>
/// OrionGuard's guards do not read <see cref="Current"/>; configuring a factory here does not change the
/// exceptions they throw. See <see cref="IExceptionFactory"/>.
/// </remarks>
[Obsolete("OrionGuard guards never read ExceptionFactoryProvider, so configuring a factory does not change the exceptions they throw. " +
          "Catch GuardException (or the specific exception type) and translate it at your boundary instead. This class will be removed in v8.")]
public static class ExceptionFactoryProvider
{
    private static volatile IExceptionFactory _factory = DefaultExceptionFactory.Instance;

    /// <summary>Current exception factory. Not consulted by OrionGuard's guards.</summary>
    public static IExceptionFactory Current => _factory;

    /// <summary>Set a custom exception factory.</summary>
    [Obsolete("OrionGuard guards never read ExceptionFactoryProvider, so configuring a factory does not change the exceptions they throw. " +
              "Catch GuardException (or the specific exception type) and translate it at your boundary instead. This method will be removed in v8.")]
    public static void Configure(IExceptionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>Reset to default factory.</summary>
    [Obsolete("OrionGuard guards never read ExceptionFactoryProvider. This method will be removed in v8.")]
    public static void Reset() => _factory = DefaultExceptionFactory.Instance;
}
