using Moongazing.OrionGuard.Testing.Validators;

namespace Moongazing.OrionGuard.Testing.Tests;

public class OrionGuardSnapshotTests
{
    /// <summary>
    /// The one test that uses the default location: it reads CreateUserValidator.verified.txt from
    /// this test's own source directory, which is also the example a consumer copies.
    /// </summary>
    [Fact]
    public Task MatchAsync_MatchesTheCommittedSnapshot()
        => OrionGuardSnapshot.Of<CreateUserValidator, CreateUserRequest>().MatchAsync();

    [Fact]
    public async Task MatchAsync_Passes_WhenSnapshotMatches()
    {
        using var directory = new TempDirectory();
        var path = directory.File("match.verified.txt");
        var snapshot = OrionGuardSnapshot.Of<CreateUserValidator, CreateUserRequest>();
        File.WriteAllText(path, snapshot.Render());

        await snapshot.MatchAsync(path);

        Assert.False(File.Exists(directory.File("match.received.txt")));
    }

    [Fact]
    public async Task MatchAsync_Throws_AndNamesTheDifference_WhenSnapshotDiffers()
    {
        using var directory = new TempDirectory();
        var path = directory.File("drift.verified.txt");
        var snapshot = OrionGuardSnapshot.Of<CreateUserValidator, CreateUserRequest>();
        var actual = snapshot.Render();
        File.WriteAllText(path, actual.Replace("Age must be greater than 0.", "Age must be positive."));

        var exception = await Assert.ThrowsAsync<ValidatorAssertionException>(() => snapshot.MatchAsync(path));

        Assert.Contains("Age must be positive.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Age must be greater than 0.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("line ", exception.Message, StringComparison.Ordinal);
        Assert.Equal(actual, File.ReadAllText(directory.File("drift.received.txt")));
    }

    [Fact]
    public async Task MatchAsync_WritesTheSnapshotAndPasses_WhenItIsMissingAndCiIsOff()
    {
        using var directory = new TempDirectory();
        var path = directory.File("fresh.verified.txt");
        var snapshot = OrionGuardSnapshot.Of<CreateUserValidator, CreateUserRequest>();

        await snapshot.MatchAsync(path);

        Assert.Equal(snapshot.Render(), File.ReadAllText(path));
    }

    [Fact]
    public async Task MatchAsync_Throws_WhenSnapshotIsMissingAndCiIsOn()
    {
        using var directory = new TempDirectory();
        var path = directory.File("fresh.verified.txt");
        var snapshot = OrionGuardSnapshot.Of<CreateUserValidator, CreateUserRequest>();

        var exception = await Assert.ThrowsAsync<ValidatorAssertionException>(
            () => snapshot.MatchAsync(path, ci: true));

        Assert.Contains(nameof(CreateUserValidator), exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(path));
        Assert.Equal(snapshot.Render(), File.ReadAllText(directory.File("fresh.received.txt")));
    }

    [Fact]
    public void Render_ReportsProbesAndAcceptedValuesPerProperty()
    {
        var text = OrionGuardSnapshot.Of<CreateUserValidator, CreateUserRequest>().Render();

        Assert.Contains("[Email]\n  null -> EMAIL: Email is required.\n", text, StringComparison.Ordinal);
        Assert.Contains("[Age]\n  -1 -> AGE: Age must be greater than 0.\n", text, StringComparison.Ordinal);
        Assert.Contains("  1 -> accepted\n", text, StringComparison.Ordinal);
        // Notes carries no rule, so every probe value for it is accepted.
        Assert.Contains("[Notes]\n  null -> accepted\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ReportsAsyncOnlyRules()
    {
        // AbstractValidator<T>.Validate runs only the sync rules, so probing through it would
        // report this validator as reporting nothing at all.
        var text = OrionGuardSnapshot.Of<AsyncOnlyValidator, CreateUserRequest>().Render();

        Assert.Contains("[Email]\n  null -> EMAIL_ASYNC: Email is required.\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ReportsSyncAndAsyncRulesOnce_ForAMixedValidator()
    {
        var text = OrionGuardSnapshot.Of<MixedRulesValidator, CreateUserRequest>().Render();

        Assert.Contains("[Age]\n  -1 -> AGE_ASYNC: Age must be greater than 0.\n", text, StringComparison.Ordinal);
        Assert.Contains("[Name]\n  null -> NAME: Name is required.\n", text, StringComparison.Ordinal);
        // ValidateAsync runs the sync rules too, so the sync rule must not be reported twice.
        Assert.Equal(1, CountOf(text, "  null -> NAME: Name is required.\n"));
    }

    [Fact]
    public void Render_ProbesConcreteCollectionTypesWithAnEmptyAndASingleItemValue()
    {
        var text = OrionGuardSnapshot.Of<TagsValidator, TagsRequest>().Render();

        Assert.Contains(
            "[Metadata]\n  null -> accepted\n  [] -> META: Metadata must not be empty when supplied.\n  [one] -> accepted\n",
            text, StringComparison.Ordinal);
        Assert.Contains(
            "[Tags]\n  null -> accepted\n  [] -> TAGS: Tags must not be empty when supplied.\n  [one] -> accepted\n",
            text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SaysSo_WhenNoValueOfTheCollectionTypeCanBeConstructed()
    {
        var text = OrionGuardSnapshot.Of<TagsValidator, TagsRequest>().Render();

        // ReadOnlyCollection<string> has no parameterless constructor, so only null can be probed.
        Assert.Contains(
            "[Items]\n  null -> ITEMS: Items is required.\n  (no value of this type could be constructed to probe with)\n",
            text, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly string path = Path.Combine(
            Path.GetTempPath(), "orionguard-snapshot-tests", Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(path);

        public string File(string name) => Path.Combine(path, name);

        public void Dispose() => Directory.Delete(path, recursive: true);
    }
}
