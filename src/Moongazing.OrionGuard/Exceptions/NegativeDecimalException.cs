namespace Moongazing.OrionGuard.Exceptions;

/// <summary>
/// Exception thrown when a negative decimal value is provided.
/// </summary>
public sealed class NegativeDecimalException : GuardException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NegativeDecimalException"/> class.
    /// </summary>
    /// <param name="parameterName">The parameter name for the negative decimal value.</param>
    public NegativeDecimalException(string parameterName)
        : base($"{parameterName} cannot be a negative decimal value.", parameterName, "NOT_NEGATIVE_DECIMAL") { }
}
