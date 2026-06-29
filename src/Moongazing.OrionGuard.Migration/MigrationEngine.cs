using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Moongazing.OrionGuard.Migration;

/// <summary>
/// Drives the migration of source text: parse with Roslyn, rewrite FluentValidation validators,
/// and return a <see cref="FileMigrationResult"/>. The engine is pure with respect to the file
/// system -- it operates on text in and text out -- so it is trivially testable and the CLI owns
/// all reading and writing.
/// </summary>
public static class MigrationEngine
{
    /// <summary>
    /// Migrates a single file's source text.
    /// </summary>
    /// <param name="filePath">Absolute path of the file (used only in findings, not read).</param>
    /// <param name="sourceText">The file's source text.</param>
    /// <returns>The migration result, including any constructs left for manual follow-up.</returns>
    public static FileMigrationResult Migrate(string filePath, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(sourceText);

        var text = SourceText.From(sourceText);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetRoot();

        var rewriter = new ValidatorRewriter(filePath, text);
        var newRoot = rewriter.Visit(root);

        // If the file declares no FluentValidation validator at all, return it unchanged so callers
        // can cheaply skip it; the rewriter only edits validator classes it positively identified.
        var migratedText = rewriter.TouchedAnyValidator
            ? newRoot!.ToFullString()
            : sourceText;

        return new FileMigrationResult(filePath, sourceText, migratedText, rewriter.Findings);
    }
}
