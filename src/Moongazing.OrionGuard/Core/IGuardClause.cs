namespace Moongazing.OrionGuard.Core;

/// <summary>
/// Represents a validation clause that can be executed.
/// </summary>
public interface IGuardClause
{
    /// <summary>
    /// Executes the validation logic for this clause.
    /// </summary>
    void Validate();
}

/// <summary>
/// Defines the contract for fluent guard step operations.
/// </summary>
public interface IFluentGuardStep<T>
{
    IFluentGuardStep<T> NotNull();
    IFluentGuardStep<T> NotEmpty();
    IFluentGuardStep<T> Length(int min, int max);
    IFluentGuardStep<T> Matches(string pattern);
    T Value { get; }
}
