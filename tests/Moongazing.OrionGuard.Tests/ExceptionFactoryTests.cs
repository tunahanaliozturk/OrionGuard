using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.Tests;

public class ExceptionFactoryTests : IDisposable
{
    public ExceptionFactoryTests()
    {
        // Reset to default before each test to avoid cross-test contamination.
        ExceptionFactoryProvider.Reset();
    }

    public void Dispose()
    {
        ExceptionFactoryProvider.Reset();
    }

    #region DefaultExceptionFactory

    [Fact]
    public void DefaultFactory_ShouldCreateArgumentNullException_ForNotNullCode()
    {
        var factory = DefaultExceptionFactory.Instance;
        var exception = factory.CreateException("NOT_NULL", "param", "Value cannot be null.");

        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public void DefaultFactory_ShouldCreateArgumentOutOfRangeException_ForOutOfRangeCode()
    {
        var factory = DefaultExceptionFactory.Instance;
        var exception = factory.CreateException("OUT_OF_RANGE", "param", "Value out of range.");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void DefaultFactory_ShouldCreateArgumentOutOfRangeException_ForGreaterThanCode()
    {
        var factory = DefaultExceptionFactory.Instance;
        var exception = factory.CreateException("GREATER_THAN", "param", "Must be greater.");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void DefaultFactory_ShouldCreateArgumentOutOfRangeException_ForLessThanCode()
    {
        var factory = DefaultExceptionFactory.Instance;
        var exception = factory.CreateException("LESS_THAN", "param", "Must be less.");

        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Fact]
    public void DefaultFactory_ShouldCreateArgumentException_ForUnknownCode()
    {
        var factory = DefaultExceptionFactory.Instance;
        var exception = factory.CreateException("INVALID_EMAIL", "email", "Invalid email.");

        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void DefaultFactory_ShouldBeCaseInsensitive()
    {
        var factory = DefaultExceptionFactory.Instance;

        var upper = factory.CreateException("NOT_NULL", "p", "msg");
        var lower = factory.CreateException("not_null", "p", "msg");

        Assert.IsType<ArgumentNullException>(upper);
        Assert.IsType<ArgumentNullException>(lower);
    }

    #endregion

    #region ExceptionFactoryProvider.Configure

    [Fact]
    public void Configure_ShouldSetCustomFactory()
    {
        var customFactory = new TestExceptionFactory();

        ExceptionFactoryProvider.Configure(customFactory);

        Assert.Same(customFactory, ExceptionFactoryProvider.Current);
    }

    [Fact]
    public void Configure_ShouldThrow_WhenFactoryIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ExceptionFactoryProvider.Configure(null!));
    }

    [Fact]
    public void CustomFactory_ShouldBeUsed_AfterConfigure()
    {
        var customFactory = new TestExceptionFactory();
        ExceptionFactoryProvider.Configure(customFactory);

        var exception = ExceptionFactoryProvider.Current.CreateException("ANY", "param", "test");

        Assert.IsType<InvalidOperationException>(exception);
    }

    #endregion

    #region ExceptionFactoryProvider.Reset

    [Fact]
    public void Reset_ShouldRestoreDefaultFactory()
    {
        ExceptionFactoryProvider.Configure(new TestExceptionFactory());

        Assert.IsNotType<DefaultExceptionFactory>(ExceptionFactoryProvider.Current);

        ExceptionFactoryProvider.Reset();

        Assert.IsType<DefaultExceptionFactory>(ExceptionFactoryProvider.Current);
    }

    [Fact]
    public void Reset_ShouldBeIdempotent()
    {
        ExceptionFactoryProvider.Reset();
        ExceptionFactoryProvider.Reset();

        Assert.IsType<DefaultExceptionFactory>(ExceptionFactoryProvider.Current);
    }

    [Fact]
    public void Current_ShouldReturnDefaultFactory_Initially()
    {
        Assert.IsType<DefaultExceptionFactory>(ExceptionFactoryProvider.Current);
    }

    #endregion

    #region Test Helpers

    private sealed class TestExceptionFactory : IExceptionFactory
    {
        public Exception CreateException(string errorCode, string parameterName, string message, Exception? innerException = null)
        {
            return new InvalidOperationException($"Custom: {message}");
        }
    }

    #endregion
}
