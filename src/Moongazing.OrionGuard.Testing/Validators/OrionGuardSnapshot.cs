using System.Runtime.CompilerServices;
using System.Text;
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.DependencyInjection;

namespace Moongazing.OrionGuard.Testing.Validators;

/// <summary>
/// Entry point for validator snapshot tests: pins the rules a validator actually enforces to a
/// <c>.verified.txt</c> file so a test fails when they change without the file being updated.
/// </summary>
/// <example>
/// <code>
/// [Fact]
/// public Task CreateUserValidator_rules_are_unchanged()
///     =&gt; OrionGuardSnapshot.Of&lt;CreateUserValidator, CreateUserRequest&gt;().MatchAsync();
/// </code>
/// </example>
public static class OrionGuardSnapshot
{
    /// <summary>
    /// Starts a snapshot of what <typeparamref name="TValidator"/> reports for
    /// <typeparamref name="TModel"/>.
    /// </summary>
    /// <typeparam name="TValidator">The validator under test. Needs a public parameterless constructor.</typeparam>
    /// <typeparam name="TModel">The validated model. Needs a public parameterless constructor.</typeparam>
    public static ValidatorSnapshot<TValidator, TModel> Of<TValidator, TModel>()
        where TValidator : IValidator<TModel>, new()
        where TModel : class, new()
        => new();
}

/// <summary>
/// A snapshot of one validator's behaviour. Created by
/// <see cref="OrionGuardSnapshot.Of{TValidator, TModel}"/>.
/// </summary>
/// <typeparam name="TValidator">The validator under test.</typeparam>
/// <typeparam name="TModel">The validated model.</typeparam>
public sealed class ValidatorSnapshot<TValidator, TModel>
    where TValidator : IValidator<TModel>, new()
    where TModel : class, new()
{
    private const string VerifiedSuffix = ".verified.txt";
    private const string ReceivedSuffix = ".received.txt";

    internal ValidatorSnapshot() { }

    /// <summary>
    /// Renders the snapshot text without touching the file system: one section per property of
    /// <typeparamref name="TModel"/>, listing each probe value and the error code and message
    /// <typeparamref name="TValidator"/> reported for it, or <c>accepted</c> when it reported
    /// nothing.
    /// </summary>
    /// <remarks>
    /// The text is deterministic: properties and errors are ordered ordinally, probe values are
    /// fixed literals, numbers and dates are formatted with the invariant culture, and lines end
    /// with <c>\n</c> on every platform. The messages themselves come from the validator, so a
    /// validator that localizes its messages produces a culture-dependent snapshot.
    /// </remarks>
    public string Render()
    {
        var report = ValidatorProbe.Run<TValidator, TModel>();
        var text = new StringBuilder();

        text.Append("validator: ").Append(report.ValidatorName).Append('\n');
        text.Append("model: ").Append(report.ModelName).Append('\n');

        foreach (var property in report.Properties)
        {
            text.Append('\n').Append('[').Append(property.Property).Append("]\n");

            foreach (var outcome in property.Outcomes)
            {
                if (outcome.Thrown is not null)
                {
                    text.Append("  ").Append(outcome.Probe).Append(" -> threw ").Append(outcome.Thrown).Append('\n');
                    continue;
                }

                if (outcome.Errors.Count == 0)
                {
                    text.Append("  ").Append(outcome.Probe).Append(" -> accepted\n");
                    continue;
                }

                foreach (var error in Ordered(outcome.Errors))
                {
                    text.Append("  ").Append(outcome.Probe).Append(" -> ").Append(Describe(error)).Append('\n');
                }
            }
        }

        if (report.Unattributed.Count > 0)
        {
            // Rules whose reported parameter name matches no property: cross-cutting rules, or a
            // rule whose property name was spelled by hand and drifted from the model.
            text.Append("\n[unattributed]\n");
            foreach (var error in report.Unattributed)
            {
                var name = string.IsNullOrEmpty(error.ParameterName) ? "(none)" : error.ParameterName;
                text.Append("  ").Append(name).Append(" -> ").Append(Describe(error)).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Compares the rendered snapshot with the stored one and throws
    /// <see cref="ValidatorAssertionException"/> when they differ, after writing the rendered text
    /// to a <c>.received.txt</c> file beside the snapshot.
    /// </summary>
    /// <param name="snapshotPath">
    /// Where the snapshot lives. Defaults to <c>{TValidator}.verified.txt</c> in the directory of
    /// the calling test's source file.
    /// </param>
    /// <param name="ci">
    /// When <c>false</c> (the default) a missing snapshot is written and the test passes, so the
    /// first run creates the file for you to review and commit. Pass <c>true</c> on a build server
    /// to make a missing snapshot a failure instead -- for example
    /// <c>ci: Environment.GetEnvironmentVariable("CI") is not null</c>. No environment variable is
    /// read for you.
    /// </param>
    /// <param name="callerFilePath">Supplied by the compiler; do not pass it.</param>
    /// <exception cref="ValidatorAssertionException">
    /// The snapshot differs from what the validator reports, or it is missing and
    /// <paramref name="ci"/> is <c>true</c>.
    /// </exception>
    public async Task MatchAsync(
        string? snapshotPath = null,
        bool ci = false,
        [CallerFilePath] string callerFilePath = "")
    {
        var verified = ResolvePath(snapshotPath, callerFilePath);
        var received = ReceivedPathFor(verified);
        var actual = Render();

        if (!File.Exists(verified))
        {
            if (ci)
            {
                await WriteAsync(received, actual).ConfigureAwait(false);
                throw new ValidatorAssertionException(
                    $"No snapshot for {typeof(TValidator).Name} at '{verified}', and ci: true forbids creating one. " +
                    $"Run the test locally, review the generated snapshot, and commit it. " +
                    $"What the validator reports now was written to '{received}'.");
            }

            await WriteAsync(verified, actual).ConfigureAwait(false);
            Delete(received);
            return;
        }

        var expected = Normalize(await File.ReadAllTextAsync(verified).ConfigureAwait(false));
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Delete(received);
            return;
        }

        await WriteAsync(received, actual).ConfigureAwait(false);
        throw new ValidatorAssertionException(
            $"{typeof(TValidator).Name} no longer matches its snapshot '{verified}'.\n" +
            $"{Diff(expected, actual)}\n" +
            $"What the validator reports now was written to '{received}'. " +
            $"If the change is intended, copy it over the snapshot.");
    }

    private static string ResolvePath(string? snapshotPath, string callerFilePath)
    {
        if (!string.IsNullOrEmpty(snapshotPath))
        {
            return snapshotPath;
        }

        var directory = string.IsNullOrEmpty(callerFilePath) ? null : Path.GetDirectoryName(callerFilePath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ValidatorAssertionException(
                $"Could not place the snapshot for {typeof(TValidator).Name}: the caller's source path is " +
                $"unavailable, which happens when MatchAsync is not called directly from a test. " +
                $"Pass snapshotPath explicitly.");
        }

        return Path.Combine(directory, typeof(TValidator).Name + VerifiedSuffix);
    }

    private static string ReceivedPathFor(string verified)
        => verified.EndsWith(VerifiedSuffix, StringComparison.Ordinal)
            ? verified[..^VerifiedSuffix.Length] + ReceivedSuffix
            : verified + ReceivedSuffix;

    /// <summary>
    /// Names the first few differing lines. A snapshot diff is read by a human staring at a test
    /// failure, so it shows line numbers and both texts rather than the whole file.
    /// </summary>
    private static string Diff(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var report = new StringBuilder();
        var shown = 0;

        for (var index = 0; index < Math.Max(expectedLines.Length, actualLines.Length) && shown < 3; index++)
        {
            var expectedLine = index < expectedLines.Length ? expectedLines[index] : null;
            var actualLine = index < actualLines.Length ? actualLines[index] : null;
            if (string.Equals(expectedLine, actualLine, StringComparison.Ordinal)) continue;

            report.Append("  line ").Append(index + 1).Append(":\n");
            report.Append("    snapshot: ").Append(Show(expectedLine)).Append('\n');
            report.Append("    actual:   ").Append(Show(actualLine)).Append('\n');
            shown++;
        }

        return report.ToString().TrimEnd('\n');

        static string Show(string? line) => line is null ? "(end of file)" : line;
    }

    private static IEnumerable<ValidationError> Ordered(IReadOnlyList<ValidationError> errors)
        => errors
            .OrderBy(error => error.ErrorCode ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(error => error.Message, StringComparer.Ordinal);

    private static string Describe(ValidationError error)
    {
        var text = $"{error.ErrorCode ?? "(no code)"}: {error.Message}";
        return error.Severity == Severity.Error ? text : $"{text} ({error.Severity})";
    }

    /// <summary>
    /// Writes UTF-8 without a BOM and with <c>\n</c> line endings, so the file a Windows machine
    /// writes is byte-identical to the one a Linux build server writes.
    /// </summary>
    private static async Task WriteAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            .ConfigureAwait(false);
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Folds CRLF and lone CR to LF so a snapshot checked out with Windows line endings still
    /// matches, and strips a BOM left by an editor that saved the file.
    /// </summary>
    private static string Normalize(string text)
        => text.TrimStart('﻿').Replace("\r\n", "\n").Replace("\r", "\n");
}
