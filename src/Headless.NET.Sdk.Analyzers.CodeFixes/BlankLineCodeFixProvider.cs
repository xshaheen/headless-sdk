using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;

namespace Headless.NET.Sdk.Analyzers;

/// <summary>Inserts the blank line that HLS0001 or HLS0002 asks for.</summary>
/// <remarks>
/// The blank line is inserted directly after the previous statement's line break, above any
/// comments attached to the next statement, and holds no whitespace. That is exactly the shape
/// CSharpier prints, so formatting after the fix changes nothing.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlankLineCodeFixProvider)), Shared]
public sealed class BlankLineCodeFixProvider : CodeFixProvider
{
    private const string _Title = "Add blank line";

    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create(
            BlankLineDescriptors.BeforeControlTransferId,
            BlankLineDescriptors.AroundMultilineStatementId
        );

    public override FixAllProvider GetFixAllProvider() => BlankLineFixAllProvider.Instance;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    _Title,
                    cancellationToken => _AddBlankLinesAsync(context.Document, [diagnostic], cancellationToken),
                    equivalenceKey: _Title
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> _AddBlankLinesAsync(
        Document document,
        IEnumerable<Diagnostic> diagnostics,
        CancellationToken cancellationToken
    )
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            return document;
        }

        // HLS0001 and HLS0002 can both report one gap; a set keeps that to one inserted line.
        var targets = new HashSet<SyntaxToken>();

        foreach (var diagnostic in diagnostics)
        {
            var token = root.FindToken(diagnostic.Location.SourceSpan.Start);

            if (
                diagnostic.Properties.TryGetValue(BlankLineDescriptors.PlacementProperty, out var placement)
                && placement == BlankLineDescriptors.PlacementAfter
            )
            {
                token = token.GetNextToken();
            }

            if (!token.IsKind(SyntaxKind.None))
            {
                targets.Add(token);
            }
        }

        if (targets.Count == 0)
        {
            return document;
        }

        var newRoot = root.ReplaceTokens(targets, static (original, _) => _WithLeadingBlankLine(original));

        return document.WithSyntaxRoot(newRoot);
    }

    private static SyntaxToken _WithLeadingBlankLine(SyntaxToken token)
    {
        // Reuse the previous line's own line break so a CRLF file stays CRLF.
        var lineBreak = token
            .GetPreviousToken()
            .TrailingTrivia.LastOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));

        if (lineBreak.IsKind(SyntaxKind.None))
        {
            return token;
        }

        return token.WithLeadingTrivia(token.LeadingTrivia.Insert(0, lineBreak));
    }

    private sealed class BlankLineFixAllProvider : DocumentBasedFixAllProvider
    {
        public static readonly BlankLineFixAllProvider Instance = new();

        protected override string GetFixAllTitle(FixAllContext fixAllContext) => _Title;

        protected override async Task<Document?> FixAllAsync(
            FixAllContext fixAllContext,
            Document document,
            ImmutableArray<Diagnostic> diagnostics
        ) => await _AddBlankLinesAsync(document, diagnostics, fixAllContext.CancellationToken).ConfigureAwait(false);
    }
}
