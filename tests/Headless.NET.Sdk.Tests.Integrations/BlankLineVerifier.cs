using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Headless.NET.Sdk.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

// A minimal analyzer and code-fix harness. The rules are syntax-only, so a compilation without
// metadata references reports exactly what a consumer build does; the semantic errors that
// leaves behind are irrelevant to them. Expected diagnostics are marked inline as
// {|HLS0001:return|}, the markup convention of Microsoft.CodeAnalysis.Testing.
internal static class BlankLineVerifier
{
    private const int _MaxFixIterations = 50;

    public static async Task VerifyDiagnosticsAsync(string markup, CancellationToken cancellationToken)
    {
        var (source, expected) = ParseMarkup(markup);
        var document = CreateDocument(source);
        var actual = await GetDiagnosticsAsync(document, cancellationToken);
        var text = SourceText.From(source);

        Assert.Equal(Describe(expected, text), Describe(actual.Select(d => (d.Id, d.Location.SourceSpan)), text));
    }

    // Applies the fixes one diagnostic at a time and through Fix All, and requires both to produce
    // `expected`, to leave no diagnostic behind, and to be stable under CSharpier.
    public static async Task VerifyFixAsync(string markup, string expected, CancellationToken cancellationToken)
    {
        await VerifyDiagnosticsAsync(markup, cancellationToken);
        var (source, _) = ParseMarkup(markup);

        var fixedOneByOne = await ApplyFixesOneByOneAsync(CreateDocument(source), cancellationToken);
        var fixedAll = await ApplyFixAllAsync(CreateDocument(source), cancellationToken);

        Assert.Equal(expected, fixedOneByOne);
        Assert.Equal(expected, fixedAll);
        Assert.Empty(await GetDiagnosticsAsync(CreateDocument(expected), cancellationToken));
        Assert.Equal(expected, await CSharpier.FormatAsync(expected, cancellationToken));
    }

    public static Document CreateDocument(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Test", LanguageNames.CSharp);

        return workspace.AddDocument(project.Id, "Test.cs", SourceText.From(source));
    }

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        Document document,
        CancellationToken cancellationToken
    )
    {
        var tree = await document.GetSyntaxTreeAsync(cancellationToken);
        Assert.NotNull(tree);
        Assert.Empty(tree.GetDiagnostics(cancellationToken));

        var compilation = await document.Project.GetCompilationAsync(cancellationToken);
        Assert.NotNull(compilation);

        var diagnostics = await compilation
            .WithAnalyzers([new BlankLineAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);

        return [.. diagnostics.OrderBy(d => d.Location.SourceSpan.Start).ThenBy(d => d.Id, StringComparer.Ordinal)];
    }

    private static async Task<string> ApplyFixesOneByOneAsync(Document document, CancellationToken cancellationToken)
    {
        var provider = new BlankLineCodeFixProvider();

        for (var iteration = 0; iteration < _MaxFixIterations; iteration++)
        {
            var diagnostics = await GetDiagnosticsAsync(document, cancellationToken);

            if (diagnostics.IsEmpty)
            {
                return (await document.GetTextAsync(cancellationToken)).ToString();
            }

            var actions = new List<CodeAction>();
            var context = new CodeFixContext(
                document,
                diagnostics[0],
                (action, _) => actions.Add(action),
                cancellationToken
            );
            await provider.RegisterCodeFixesAsync(context);

            document = await ApplyAsync(Assert.Single(actions), document, cancellationToken);
        }

        throw new InvalidOperationException($"The code fix did not converge within {_MaxFixIterations} iterations.");
    }

    private static async Task<string> ApplyFixAllAsync(Document document, CancellationToken cancellationToken)
    {
        var provider = new BlankLineCodeFixProvider();
        var fixAllProvider = provider.GetFixAllProvider();
        Assert.NotNull(fixAllProvider);

        var context = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            "Add blank line",
            provider.FixableDiagnosticIds,
            new DocumentDiagnosticProvider(),
            cancellationToken
        );
        var action = await fixAllProvider.GetFixAsync(context);
        Assert.NotNull(action);

        var fixedDocument = await ApplyAsync(action, document, cancellationToken);

        return (await fixedDocument.GetTextAsync(cancellationToken)).ToString();
    }

    private static async Task<Document> ApplyAsync(
        CodeAction action,
        Document document,
        CancellationToken cancellationToken
    )
    {
        var operations = await action.GetOperationsAsync(cancellationToken);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;

        return solution.GetDocument(document.Id)!;
    }

    // Parses {|ID:text|} markers, nesting allowed, into the plain source and the marked spans.
    private static (string Source, List<(string Id, TextSpan Span)> Expected) ParseMarkup(string markup)
    {
        var source = new StringBuilder(markup.Length);
        var expected = new List<(string Id, TextSpan Span)>();
        var open = new Stack<(string Id, int Start)>();

        for (var i = 0; i < markup.Length; i++)
        {
            if (markup[i] == '{' && i + 1 < markup.Length && markup[i + 1] == '|')
            {
                var colon = markup.IndexOf(':', i);
                open.Push((markup[(i + 2)..colon], source.Length));
                i = colon;
            }
            else if (markup[i] == '|' && i + 1 < markup.Length && markup[i + 1] == '}')
            {
                var (id, start) = open.Pop();
                expected.Add((id, TextSpan.FromBounds(start, source.Length)));
                i++;
            }
            else
            {
                source.Append(markup[i]);
            }
        }

        Assert.Empty(open);

        return (source.ToString(), expected);
    }

    private static string[] Describe(IEnumerable<(string Id, TextSpan Span)> diagnostics, SourceText text) =>
        [
            .. diagnostics
                .OrderBy(d => d.Span.Start)
                .ThenBy(d => d.Id, StringComparer.Ordinal)
                .Select(d =>
                {
                    var position = text.Lines.GetLinePosition(d.Span.Start);

                    return $"{d.Id} at {position.Line + 1}:{position.Character + 1} '{text.ToString(d.Span)}'";
                }),
        ];

    private sealed class DocumentDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document,
            CancellationToken cancellationToken
        ) => await GetDiagnosticsAsync(document, cancellationToken);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken
        ) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken
        )
        {
            var diagnostics = new List<Diagnostic>();

            foreach (var document in project.Documents)
            {
                diagnostics.AddRange(await GetDiagnosticsAsync(document, cancellationToken));
            }

            return diagnostics;
        }
    }

    // Runs the CSharpier build that CSharpier.MSBuild restores for every project here, so the
    // stability check uses exactly the formatter version the repository pins, with no tool restore.
    public static class CSharpier
    {
        public static async Task<string> FormatAsync(string source, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetTempPath(),
            };
            startInfo.ArgumentList.Add(LocateCSharpier());
            startInfo.ArgumentList.Add("format");

            using var process = Process.Start(startInfo)!;
            await process.StandardInput.WriteAsync(source.AsMemory(), cancellationToken);
            process.StandardInput.Close();

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            Assert.True(process.ExitCode == 0, $"CSharpier exited with {process.ExitCode}: {await error}");

            return await output;
        }

        private static string LocateCSharpier()
        {
            var repositoryRoot = TestRepository.FindRoot("CSharpier location");
            var version = XDocument
                .Load(Path.Combine(repositoryRoot, "Directory.Packages.props"))
                .Descendants("GlobalPackageReference")
                .Single(element => element.Attribute("Include")?.Value == "CSharpier.MSBuild")
                .Attribute("Version")!
                .Value;
            var packages =
                Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            var toolDirectory = Path.Combine(packages, "csharpier.msbuild", version, "tools", "csharpier");
            var executable = Directory
                .EnumerateDirectories(toolDirectory, "net*")
                .Select(directory => Path.Combine(directory, "CSharpier.dll"))
                .Where(File.Exists)
                .OrderByDescending(path => Version.Parse(Path.GetFileName(Path.GetDirectoryName(path))![3..]))
                .FirstOrDefault();

            Assert.True(executable is not null, $"CSharpier {version} is not restored under {toolDirectory}.");

            return executable;
        }
    }
}
