namespace Moongazing.OrionGuard.Migration;

/// <summary>
/// A single construct the codemod could not safely migrate and therefore left untouched
/// (annotated with a TODO marker in the rewritten source) rather than guessing at a translation.
/// </summary>
/// <param name="FilePath">Absolute path of the file the construct was found in.</param>
/// <param name="Line">1-based line number of the construct in the original source.</param>
/// <param name="Rule">The FluentValidation rule or construct name, for example <c>SetValidator</c>.</param>
/// <param name="Reason">A short, human-readable explanation of why it was not migrated.</param>
public sealed record MigrationFinding(string FilePath, int Line, string Rule, string Reason);
