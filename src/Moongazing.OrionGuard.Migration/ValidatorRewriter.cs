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
    private bool _swappedFluentValidationUsing;

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
    public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var visited = (CompilationUnitSyntax)base.VisitCompilationUnit(node)!;

        // The renamed base type (FluentStyleValidator<T>) is unqualified, so the migrated file must
        // import the OrionGuard compatibility namespace to compile. Normally that import is produced
        // by swapping the file's `using FluentValidation;` directive. When the source had no such
        // directive (the base type was fully qualified, or the import was global/implicit), nothing
        // was swapped -- so add the using here to keep the output compilable.
        if (TouchedAnyValidator &&
            !_swappedFluentValidationUsing &&
            !HasOrionGuardCompatibilityUsing(visited))
        {
            visited = AddOrionGuardCompatibilityUsing(visited);
        }

        return visited;
    }

    /// <inheritdoc />
    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Name is not null &&
            node.Name.ToString() == FluentValidationNamespace)
        {
            _swappedFluentValidationUsing = true;

            var replacement = SyntaxFactory.ParseName(OrionGuardCompatibilityNamespace)
                .WithLeadingTrivia(node.Name.GetLeadingTrivia())
                .WithTrailingTrivia(node.Name.GetTrailingTrivia());

            return node.WithName(replacement);
        }

        return base.VisitUsingDirective(node);
    }

    private static bool HasOrionGuardCompatibilityUsing(CompilationUnitSyntax node)
    {
        foreach (var directive in node.Usings)
        {
            if (directive.Name is not null &&
                directive.Name.ToString() == OrionGuardCompatibilityNamespace)
            {
                return true;
            }
        }

        return false;
    }

    private static CompilationUnitSyntax AddOrionGuardCompatibilityUsing(CompilationUnitSyntax node)
    {
        // Parse a fully-formed directive (keyword spacing, semicolon, trailing newline) rather than
        // assembling tokens by hand, so the inserted line is always well formed regardless of how
        // the source was laid out.
        var directive = SyntaxFactory
            .ParseCompilationUnit($"using {OrionGuardCompatibilityNamespace};\r\n")
            .Usings[0];

        return node.WithUsings(node.Usings.Insert(0, directive));
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
        if (node.Expression is not InvocationExpressionSyntax invocation)
        {
            return base.VisitExpressionStatement(node);
        }

        // RuleForEach(...) and Include(...) are validator-level constructs the codemod cannot
        // safely translate. They must be REPORTED (TODO marker + summary finding) rather than
        // silently skipped, so the user knows that part of the validator was not migrated.
        if (TryGetUnsupportedAnchor(invocation, out var anchorName, out var anchorNode))
        {
            var finding = new MigrationFinding(
                _filePath,
                GetLine(anchorNode),
                anchorName,
                UnsupportedAnchorReason(anchorName));

            _findings.Add(finding);
            return AnnotateUnmigrated(node, finding);
        }

        // Only rewrite statements that are a RuleFor(...) chain. Everything else is preserved.
        if (!TryGetRuleForChain(invocation, out var ruleCalls))
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
                TryGetAbstractValidatorGeneric(simple.Type) is not null)
            {
                return simple;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the <c>AbstractValidator&lt;T&gt;</c> generic node from a base type, whether it is
    /// written bare (<c>AbstractValidator&lt;T&gt;</c>) or fully qualified
    /// (<c>FluentValidation.AbstractValidator&lt;T&gt;</c>). Returns null for any other base type.
    /// </summary>
    private static GenericNameSyntax? TryGetAbstractValidatorGeneric(TypeSyntax type) => type switch
    {
        GenericNameSyntax g when g.Identifier.Text == FluentValidationBaseType => g,
        QualifiedNameSyntax { Right: GenericNameSyntax g }
            when g.Identifier.Text == FluentValidationBaseType => g,
        _ => null,
    };

    private static ClassDeclarationSyntax RewriteBaseType(ClassDeclarationSyntax node)
    {
        // Re-find the base inside the (possibly body-rewritten) node so we replace the live node.
        var liveBase = FindFluentValidationBase(node);
        if (liveBase is null)
        {
            return node;
        }

        var generic = TryGetAbstractValidatorGeneric(liveBase.Type);
        if (generic is null)
        {
            return node;
        }

        // Emit the unqualified OrionGuard base type and replace the whole base-type node. Replacing
        // the entire node (not just the identifier) collapses a qualified name such as
        // FluentValidation.AbstractValidator<T> down to FluentStyleValidator<T>; the compatibility
        // using added at the compilation-unit level keeps that unqualified name resolvable.
        var renamed = generic
            .WithIdentifier(SyntaxFactory.Identifier(OrionGuardBaseType))
            .WithLeadingTrivia(liveBase.Type.GetLeadingTrivia())
            .WithTrailingTrivia(liveBase.Type.GetTrailingTrivia());

        return node.ReplaceNode(liveBase.Type, renamed);
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

    /// <summary>
    /// Detects a validator-level construct that is recognised but deliberately not auto-migrated
    /// (currently <c>RuleForEach</c> and <c>Include</c>). Walks to the innermost callee so a chain
    /// such as <c>RuleForEach(x => x.Items).SetValidator(...)</c> is matched on its anchor.
    /// </summary>
    private static bool TryGetUnsupportedAnchor(
        InvocationExpressionSyntax outer, out string anchorName, out SyntaxNode anchorNode)
    {
        var current = outer;

        while (true)
        {
            if (TryGetCalleeName(current.Expression, out var name, out var nameNode) &&
                IsUnsupportedAnchorName(name))
            {
                anchorName = name;
                anchorNode = nameNode;
                return true;
            }

            // Descend through a member-access chain (.Method(...).Method(...)) toward the anchor.
            if (current.Expression is MemberAccessExpressionSyntax
                {
                    Expression: InvocationExpressionSyntax inner,
                })
            {
                current = inner;
                continue;
            }

            anchorName = string.Empty;
            anchorNode = outer;
            return false;
        }
    }

    private static bool TryGetCalleeName(
        ExpressionSyntax callee, out string name, out SyntaxNode nameNode)
    {
        switch (callee)
        {
            case IdentifierNameSyntax identifier:
                name = identifier.Identifier.Text;
                nameNode = identifier;
                return true;
            case MemberAccessExpressionSyntax access:
                name = access.Name.Identifier.Text;
                nameNode = access.Name;
                return true;
            default:
                name = string.Empty;
                nameNode = callee;
                return false;
        }
    }

    private static bool IsUnsupportedAnchorName(string name) =>
        name is "RuleForEach" or "Include";

    private static string UnsupportedAnchorReason(string anchorName) => anchorName switch
    {
        "RuleForEach" => "RuleForEach(...) collection rules are not auto-migrated",
        "Include" => "Include(...) of another validator is not auto-migrated",
        _ => $"{anchorName}(...) is not auto-migrated",
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
