using System.Text.RegularExpressions;
using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Compatibility;

namespace Moongazing.OrionGuard.Tests;

/// <summary>
/// User-supplied patterns run against untrusted input, so every regex a validator evaluates
/// must carry a match timeout. A catastrophic-backtracking pattern has to fail fast with
/// <see cref="RegexMatchTimeoutException"/> instead of pinning the calling thread.
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
    public void RegexAttribute_ShouldTimeOut_WhenPatternBacktracksCatastrophically()
    {
        var attribute = new RegexAttribute(CatastrophicPattern);

        var exception = RunBounded(() => attribute.IsValid(HostileInput));

        Assert.IsType<RegexMatchTimeoutException>(exception);
    }

    [Fact]
    public void FluentStyleValidatorMatches_ShouldTimeOut_WhenPatternBacktracksCatastrophically()
    {
        var validator = new CatastrophicPatternValidator();

        var exception = RunBounded(() => validator.Validate(new Input(HostileInput)));

        Assert.IsType<RegexMatchTimeoutException>(exception);
    }

    [Fact]
    public void RegexAttribute_ShouldStillMatch_WhenInputIsBenign()
    {
        var attribute = new RegexAttribute("^[A-Z]{2}$");

        Assert.True(attribute.IsValid("TR"));
        Assert.False(attribute.IsValid("tr"));
    }

    private static Exception? RunBounded(Action action)
    {
        var task = Task.Run(action);
        var finished = Task.WaitAny(new Task[] { task }, Bound) == 0;
        Assert.True(finished, $"Regex evaluation did not finish within {Bound.TotalSeconds}s; the match timeout is missing.");
        return task.Exception?.InnerException;
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
