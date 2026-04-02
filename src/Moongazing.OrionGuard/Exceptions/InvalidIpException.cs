namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when an invalid IP address is provided.
/// </summary>
public sealed class InvalidIpException : GuardException
{
    public InvalidIpException(string parameterName)
        : base($"{parameterName} is not a valid IP address.", parameterName, "INVALID_IP") { }
}
