using System.Text.RegularExpressions;
using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Compatibility;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DynamicRules;
using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Tests;

/// <summary>
/// User-supplied patterns run against untrusted input, so every regex a validator evaluates
/// must carry a match timeout. A catastrophic-backtracking pattern has to fail fast instead of
/// pinning the calling thread. APIs that return a result report the timed-out value as invalid;
/// throwing guards throw their own validation exception, never <see cref="RegexMatchTimeoutException"/>.
/// </summary>
public class RegexTimeoutTests
{
    // Nested quantifier: exponential backtracking once the trailing "!" breaks the match.
    private const string CatastrophicPattern = "^(a+)+$";
    private static readonly string HostileInput = new string('a', 32) + "!";

    // Generous bound: the match timeout is 1 second; without a timeout the call would not
    // return for minutes, so a regression fails here rather than hanging the test run.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);

    [Fact]
    public void RegexAttribute_ShouldReportInvalid_WhenPatternBacktracksCatastrophically()
    {
        var attribute = new RegexAttribute(CatastrophicPattern);

        var isValid = RunBounded(() => attribute.IsValid(HostileInput));

        Assert.False(isValid);
    }

    [Fact]
    public void FluentStyleValidatorMatches_ShouldReportError_WhenPatternBacktracksCatastrophically()
    {
        var validator = new CatastrophicPatternValidator();

        var result = RunBounded(() => validator.Validate(new Input(HostileInput)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void EnsureAccumulateMatches_ShouldReportError_WhenPatternBacktracksCatastrophically()
    {
        var result = RunBounded(() => Ensure.Accumulate(HostileInput, "value").Matches(CatastrophicPattern).ToResult());

        Assert.False(result.IsValid);
        Assert.Equal("PATTERN", Assert.Single(result.Errors).ErrorCode);
    }

    [Fact]
    public void DynamicValidatorRegex_ShouldReportError_WhenPatternBacktracksCatastrophically()
    {
        var validator = DynamicValidator.FromJson(
            """{"Name":"r","Rules":[{"PropertyName":"Value","RuleType":"Regex","Parameters":{"Pattern":"^(a+)+$"}}]}""");

        var result = RunBounded(() => validator.Validate(new Input(HostileInput)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void EnsureThatMatches_ShouldThrowGuardException_WhenPatternBacktracksCatastrophically()
    {
        var exception = RunBounded(() => Record.Exception(() => Ensure.That(HostileInput).Matches(CatastrophicPattern)));

        Assert.IsType<GuardException>(exception);
    }

    [Fact]
    public void GuardForMatches_ShouldThrowRegexMismatchException_WhenPatternBacktracksCatastrophically()
    {
        var exception = RunBounded(() => Record.Exception(() => Guard.For(HostileInput, "value").Matches(CatastrophicPattern)));

        Assert.IsType<RegexMismatchException>(exception);
    }

    [Fact]
    public void AgainstRegexMismatch_ShouldThrowArgumentException_WhenPatternBacktracksCatastrophically()
    {
        var exception = RunBounded(() => Record.Exception(() => HostileInput.AgainstRegexMismatch(CatastrophicPattern, "value")));

        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void RegexAttribute_ShouldStillMatch_WhenInputIsBenign()
    {
        var attribute = new RegexAttribute("^[A-Z]{2}$");

        Assert.True(attribute.IsValid("TR"));
        Assert.False(attribute.IsValid("tr"));
    }

    private static TResult RunBounded<TResult>(Func<TResult> action)
    {
        var task = Task.Run(action);
        var finished = Task.WaitAny(new Task[] { task }, Bound) == 0;
        Assert.True(finished, $"Regex evaluation did not finish within {Bound.TotalSeconds}s; the match timeout is missing.");
        return task.GetAwaiter().GetResult();
    }

    private sealed record Input(string Value);

    private sealed class CatastrophicPatternValidator : FluentStyleValidator<Input>
    {
        public CatastrophicPatternValidator()
        {
            RuleFor(x => x.Value).Matches(CatastrophicPattern);
        }
    }
}
