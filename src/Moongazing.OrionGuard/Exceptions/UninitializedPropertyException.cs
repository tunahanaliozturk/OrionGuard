namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when an object contains uninitialized (null) properties.
/// </summary>
public sealed class UninitializedPropertyException : GuardException
{
    public UninitializedPropertyException(string parameterName, string propertyName)
        : base($"{parameterName} contains uninitialized property: {propertyName}.", parameterName, "UNINITIALIZED_PROPERTY")
    {
    }
}