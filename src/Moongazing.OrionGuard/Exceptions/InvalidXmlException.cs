namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when invalid XML content is provided.
/// </summary>
public sealed class InvalidXmlException : GuardException
{
    public InvalidXmlException(string parameterName)
        : base($"{parameterName} is not a valid XML.", parameterName, "INVALID_XML") { }
}
