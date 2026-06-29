using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Moongazing.OrionGuard.Migration;

/// <summary>
/// A Roslyn syntax rewriter that migrates FluentValidation validators to the OrionGuard
/// compatibility surface. It rewrites the validator base type, swaps the FluentValidation
/// <c>using</c> directive for the OrionGuard one, and rewrites each fully-supported
/// <c>RuleFor(...)</c> chain. A chain that contains any rule with no safe equivalent is left
/// byte-for-byte untouched, annotated with a TODO marker, and reported -- the codemod never
/// rewrites a chain partially, because dropping or mistranslating a single rule would silently
/// weaken validation.
/// </summary>
public sealed class ValidatorRewriter : CSharpSyntaxRewriter
{
    private const string FluentValidationBaseType = "AbstractValidator";
    private const string OrionGuardBaseType = "FluentStyleValidator";
    private const string FluentValidationNamespace = "FluentValidation";
    private const string OrionGuardCompatibilityNamespace = "Moongazing.OrionGuard.Compatibility";

    private readonly string _filePath;
    private readonly SourceText _sourceText;
    private readonly List<MigrationFinding> _findings = new();

    /// <summary>Initializes a new <see cref="ValidatorRewriter"/> for one source file.</summary>
    /// <param name="filePath">Absolute path of the file, used in findings.</param>
    /// <param name="sourceText">The file's source text, used to resolve line numbers.</param>
    public ValidatorRewriter(string filePath, SourceText sourceText)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _sourceText = sourceText ?? throw new ArgumentNullException(nameof(sourceText));
    }

    /// <summary>Constructs that were left untouched and need manual follow-up.</summary>
    public IReadOnlyList<MigrationFinding> Findings => _findings;

    /// <summary>True when this file declares at least one FluentValidation validator.</summary>
    public bool TouchedAnyValidator { get; private set; }

    /// <inheritdoc />
    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Name is not null &&
            node.Name.ToString() == FluentValidationNamespace)
        {
            var replacement = SyntaxFactory.ParseName(OrionGuardCompatibilityNamespace)
                .WithLeadingTrivia(node.Name.GetLeadingTrivia())
                .WithTrailingTrivia(node.Name.GetTrailingTrivia());

            return node.WithName(replacement);
        }

        return base.VisitUsingDirective(node);
    }

    /// <inheritdoc />
    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var baseType = FindFluentValidationBase(node);
        if (baseType is null)
        {
            // Not a FluentValidation validator; leave the class entirely alone.
            return node;
        }

        TouchedAnyValidator = true;

        // Rewrite the validator body first (RuleFor chains), then the base type. Visiting the
        // members through base.Visit keeps trivia handling consistent with the rest of the tree.
        var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

        return RewriteBaseType(visited);
    }

    /// <inheritdoc />
    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        // Only act on statements that are a RuleFor(...) chain. Everything else is preserved.
        if (node.Expression is not InvocationExpressionSyntax invocation ||
            !TryGetRuleForChain(invocation, out var ruleCalls))
        {
            return base.VisitExpressionStatement(node);
        }

        var mappings = new List<(MemberCall Call, RuleMapping Mapping)>(ruleCalls.Count);
        MigrationFinding? firstUnsupported = null;

        foreach (var call in ruleCalls)
        {
            var mapping = RuleMapper.Map(call.MethodName, call.Arguments);
            mappings.Add((call, mapping));

            if (!mapping.IsSupported && firstUnsupported is null)
            {
                firstUnsupported = new MigrationFinding(
                    _filePath,
                    GetLine(call.NameNode),
                    call.MethodName,
                    mapping.UnsupportedReason!);
            }
        }

        if (firstUnsupported is not null)
        {
            // All-or-nothing: record every unsupported rule in this chain and leave the statement
            // untouched, prefixed with a single TODO marker describing the first blocker.
            foreach (var (call, mapping) in mappings)
            {
                if (!mapping.IsSupported)
                {
                    _findings.Add(new MigrationFinding(
                        _filePath,
                        GetLine(call.NameNode),
                        call.MethodName,
                        mapping.UnsupportedReason!));
                }
            }

            return AnnotateUnmigrated(node, firstUnsupported);
        }

        // Every rule in the chain is supported: rewrite each call onto its OrionGuard equivalent.
        var rewritten = node;
        foreach (var (call, mapping) in mappings)
        {
            rewritten = ApplyMapping(rewritten, call, mapping);
        }

        return rewritten;
    }

    private static SimpleBaseTypeSyntax? FindFluentValidationBase(ClassDeclarationSyntax node)
    {
        if (node.BaseList is null)
        {
            return null;
        }

        foreach (var baseTypeSyntax in node.BaseList.Types)
        {
            if (baseTypeSyntax is SimpleBaseTypeSyntax simple &&
                simple.Type is GenericNameSyntax generic &&
                generic.Identifier.Text == FluentValidationBaseType)
            {
                return simple;
            }
        }

        return null;
    }

    private static ClassDeclarationSyntax RewriteBaseType(ClassDeclarationSyntax node)
    {
        // Re-find the base inside the (possibly body-rewritten) node so we replace the live node.
        var liveBase = FindFluentValidationBase(node);
        if (liveBase is null || liveBase.Type is not GenericNameSyntax generic)
        {
            return node;
        }

        var renamed = generic.WithIdentifier(
            SyntaxFactory.Identifier(OrionGuardBaseType));

        return node.ReplaceNode(generic, renamed);
    }

    private static ExpressionStatementSyntax ApplyMapping(
        ExpressionStatementSyntax statement, MemberCall call, RuleMapping mapping)
    {
        // Locate the live invocation node by span so edits compose across the chain.
        var liveInvocation = statement
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(i => i.Span == call.InvocationSpan);

        if (liveInvocation is null ||
            liveInvocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return statement;
        }

        var newName = SyntaxFactory.IdentifierName(mapping.TargetMethod!)
            .WithLeadingTrivia(access.Name.GetLeadingTrivia())
            .WithTrailingTrivia(access.Name.GetTrailingTrivia());

        var renamedAccess = access.WithName(newName);
        var updatedInvocation = liveInvocation.WithExpression(renamedAccess);

        if (mapping.ArgumentTransform == ArgumentTransform.DuplicateSingleArgument)
        {
            updatedInvocation = updatedInvocation.WithArgumentList(
                DuplicateSingleArgument(updatedInvocation.ArgumentList));
        }

        return statement.ReplaceNode(liveInvocation, updatedInvocation);
    }

    private static ArgumentListSyntax DuplicateSingleArgument(ArgumentListSyntax list)
    {
        var single = list.Arguments[0].WithoutTrivia();
        var secondArgument = single.WithLeadingTrivia(SyntaxFactory.Space);

        return list.WithArguments(
            SyntaxFactory.SeparatedList(new[] { single, secondArgument }));
    }

    private static ExpressionStatementSyntax AnnotateUnmigrated(
        ExpressionStatementSyntax node, MigrationFinding finding)
    {
        var leading = node.GetLeadingTrivia();

        // Avoid stacking duplicate markers if the file is migrated more than once.
        if (leading.ToFullString().Contains("TODO: OrionGuard migration", StringComparison.Ordinal))
        {
            return node;
        }

        var indent = ExtractIndent(leading);
        var comment = SyntaxFactory.Comment(
            $"// TODO: OrionGuard migration - {finding.Rule}: {finding.Reason}");

        var newLeading = leading
            .Add(comment)
            .Add(SyntaxFactory.CarriageReturnLineFeed)
            .Add(SyntaxFactory.Whitespace(indent));

        return node.WithLeadingTrivia(newLeading);
    }

    private static string ExtractIndent(SyntaxTriviaList leading)
    {
        for (var i = leading.Count - 1; i >= 0; i--)
        {
            if (leading[i].IsKind(SyntaxKind.WhitespaceTrivia))
            {
                return leading[i].ToString();
            }

            if (leading[i].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                break;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Walks an invocation chain from the outside in, confirming the innermost call is
    /// <c>RuleFor(...)</c>. On success, returns the list of rule calls applied after RuleFor, in
    /// source order (left to right).
    /// </summary>
    private static bool TryGetRuleForChain(
        InvocationExpressionSyntax outer, out IReadOnlyList<MemberCall> ruleCalls)
    {
        var calls = new List<MemberCall>();
        var current = outer;

        while (true)
        {
            // The innermost RuleFor(...) call's callee is a bare identifier (RuleFor), or a
            // member access (this.RuleFor / base.RuleFor). Detect the anchor in both shapes.
            if (IsRuleForAnchor(current.Expression))
            {
                // Reached the anchor. The collected calls were gathered outside-in, so reverse
                // them to restore source (left-to-right) order.
                calls.Reverse();
                ruleCalls = calls;
                return calls.Count > 0;
            }

            if (current.Expression is not MemberAccessExpressionSyntax access)
            {
                ruleCalls = Array.Empty<MemberCall>();
                return false;
            }

            calls.Add(new MemberCall(
                access.Name.Identifier.Text,
                access.Name,
                current.ArgumentList,
                current.Span));

            if (access.Expression is InvocationExpressionSyntax inner)
            {
                current = inner;
                continue;
            }

            ruleCalls = Array.Empty<MemberCall>();
            return false;
        }
    }

    private static bool IsRuleForAnchor(ExpressionSyntax callee) => callee switch
    {
        IdentifierNameSyntax { Identifier.Text: "RuleFor" } => true,
        MemberAccessExpressionSyntax { Name.Identifier.Text: "RuleFor" } => true,
        _ => false,
    };

    private int GetLine(SyntaxNode node) =>
        _sourceText.Lines.GetLinePosition(node.SpanStart).Line + 1;

    /// <summary>One <c>.Method(args)</c> call captured from a RuleFor chain.</summary>
    private readonly struct MemberCall
    {
        public MemberCall(
            string methodName,
            SimpleNameSyntax nameNode,
            ArgumentListSyntax arguments,
            TextSpan invocationSpan)
        {
            MethodName = methodName;
            NameNode = nameNode;
            Arguments = arguments;
            InvocationSpan = invocationSpan;
        }

        public string MethodName { get; }

        public SimpleNameSyntax NameNode { get; }

        public ArgumentListSyntax Arguments { get; }

        public TextSpan InvocationSpan { get; }
    }
}
