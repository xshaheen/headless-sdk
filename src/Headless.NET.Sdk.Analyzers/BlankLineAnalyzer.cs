using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Headless.NET.Sdk.Analyzers;

/// <summary>
/// Reports a missing blank line between two sibling statements when the second is a
/// control-transfer statement (HLS0001) or either one spans more than one line (HLS0002).
/// </summary>
/// <remarks>
/// Siblings are the statements of one block, one switch section, or the top-level statements of a
/// file. The first statement of a list never needs a blank line before it and the last never needs
/// one after it, which is why only adjacent pairs are examined. The rules only ever ask for a
/// single blank line, which CSharpier preserves; they never ask for one where CSharpier removes it.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlankLineAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableDictionary<string, string?> _AfterProperties = ImmutableDictionary<
        string,
        string?
    >.Empty.Add(BlankLineDescriptors.PlacementProperty, BlankLineDescriptors.PlacementAfter);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            BlankLineDescriptors.BeforeControlTransfer,
            BlankLineDescriptors.AroundMultilineStatement
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Blank lines are pure syntax: a tree action avoids waiting on semantic analysis and is the
        // cheapest analyzer shape Roslyn offers.
        context.RegisterSyntaxTreeAction(_AnalyzeTree);
    }

    private static void _AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        var lines = context.Tree.GetText(context.CancellationToken).Lines;

        if (root is CompilationUnitSyntax compilationUnit)
        {
            var members = compilationUnit.Members;

            for (var i = 1; i < members.Count; i++)
            {
                if (members[i - 1] is GlobalStatementSyntax previous && members[i] is GlobalStatementSyntax current)
                {
                    _AnalyzePair(context, lines, previous, current, current.Statement);
                }
            }
        }

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case BlockSyntax block:
                    _AnalyzeStatements(context, lines, block.Statements);

                    break;
                case SwitchSectionSyntax section:
                    _AnalyzeStatements(context, lines, section.Statements);

                    break;
            }
        }
    }

    private static void _AnalyzeStatements(
        SyntaxTreeAnalysisContext context,
        TextLineCollection lines,
        SyntaxList<StatementSyntax> statements
    )
    {
        for (var i = 1; i < statements.Count; i++)
        {
            _AnalyzePair(context, lines, statements[i - 1], statements[i], statements[i]);
        }
    }

    private static void _AnalyzePair(
        SyntaxTreeAnalysisContext context,
        TextLineCollection lines,
        SyntaxNode previous,
        SyntaxNode current,
        StatementSyntax currentStatement
    )
    {
        var controlTransferKeyword = _GetControlTransferKeyword(currentStatement);
        var currentIsMultiline = _IsMultiline(current, lines);
        var previousIsMultiline = !currentIsMultiline && _IsMultiline(previous, lines);

        if (controlTransferKeyword is null && !currentIsMultiline && !previousIsMultiline)
        {
            return;
        }

        var previousToken = previous.GetLastToken();
        var currentToken = current.GetFirstToken();

        if (!_LacksBlankLine(previousToken, currentToken))
        {
            return;
        }

        // Each rule reports independently, so either can be turned off without silencing the other;
        // one inserted blank line satisfies both.
        if (controlTransferKeyword is not null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    BlankLineDescriptors.BeforeControlTransfer,
                    currentToken.GetLocation(),
                    controlTransferKeyword
                )
            );
        }

        if (currentIsMultiline)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(BlankLineDescriptors.AroundMultilineStatement, currentToken.GetLocation(), "before")
            );
        }
        else if (previousIsMultiline)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    BlankLineDescriptors.AroundMultilineStatement,
                    previousToken.GetLocation(),
                    _AfterProperties,
                    "after"
                )
            );
        }
    }

    private static string? _GetControlTransferKeyword(StatementSyntax statement) =>
        statement.Kind() switch
        {
            SyntaxKind.ReturnStatement => "return",
            SyntaxKind.ThrowStatement => "throw",
            SyntaxKind.BreakStatement => "break",
            SyntaxKind.ContinueStatement => "continue",
            SyntaxKind.GotoStatement or SyntaxKind.GotoCaseStatement or SyntaxKind.GotoDefaultStatement => "goto",
            SyntaxKind.YieldReturnStatement => "yield return",
            SyntaxKind.YieldBreakStatement => "yield break",
            _ => null,
        };

    // The span excludes leading and trailing trivia, so a comment above or after a one-line
    // statement does not make it multiline.
    private static bool _IsMultiline(SyntaxNode node, TextLineCollection lines)
    {
        var span = node.Span;

        return lines.IndexOf(span.Start) != lines.IndexOf(span.End);
    }

    // True when the two tokens sit on different lines with no blank line between them. Comments
    // attached to the second statement count as part of it, so the blank line belongs above them.
    // A gap holding a preprocessor directive, disabled code, or skipped tokens is left alone:
    // where a blank line would go relative to the directive is not a question this rule can answer.
    private static bool _LacksBlankLine(SyntaxToken previousToken, SyntaxToken currentToken)
    {
        if (previousToken.IsMissing || currentToken.IsMissing)
        {
            return false;
        }

        // Two statements on one line are a layout question for the formatter, not a blank-line one.
        if (!previousToken.TrailingTrivia.Any(SyntaxKind.EndOfLineTrivia))
        {
            return false;
        }

        var lineHasContent = false;

        foreach (var trivia in currentToken.LeadingTrivia)
        {
            switch (trivia.Kind())
            {
                case SyntaxKind.WhitespaceTrivia:
                    break;
                case SyntaxKind.EndOfLineTrivia:
                    if (!lineHasContent)
                    {
                        return false;
                    }

                    lineHasContent = false;

                    break;
                case SyntaxKind.SingleLineCommentTrivia:
                case SyntaxKind.MultiLineCommentTrivia:
                case SyntaxKind.MultiLineDocumentationCommentTrivia:
                    lineHasContent = true;

                    break;

                // A /// comment's trivia ends with its own line break, so the next line starts empty.
                case SyntaxKind.SingleLineDocumentationCommentTrivia:
                    lineHasContent = false;

                    break;
                default:
                    return false;
            }
        }

        return true;
    }
}
