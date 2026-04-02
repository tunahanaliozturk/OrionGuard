using Moongazing.OrionGuard.Exceptions;
using System.Text.RegularExpressions;

namespace Moongazing.OrionGuard.Core;

public sealed class GuardBuilder<T> : IFluentGuardStep<T>
{
    public T Value { get; }
    public string ParameterName { get; }

    public GuardBuilder(T value, string parameterName)
    {
        Value = value;
        ParameterName = parameterName;
    }

    public IFluentGuardStep<T> NotNull()
    {
        if (Value is null)
            ThrowHelper.ThrowNullValue(ParameterName);
        return this;
    }

    public IFluentGuardStep<T> NotEmpty()
    {
        if (Value is string str && string.IsNullOrWhiteSpace(str))
            ThrowHelper.ThrowEmptyString(ParameterName);
        return this;
    }

    public IFluentGuardStep<T> Length(int min, int max)
    {
        if (Value is string str && (str.Length < min || str.Length > max))
            ThrowHelper.ThrowOutOfRange(ParameterName, min, max);
        return this;
    }

    public IFluentGuardStep<T> Matches(string pattern)
    {
        if (Value is string str && !RegexCache.IsMatch(str, pattern))
            ThrowHelper.ThrowRegexMismatch(ParameterName, pattern);
        return this;
    }
}